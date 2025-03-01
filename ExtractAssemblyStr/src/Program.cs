using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

public class ExtractedString {
    public string Key { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    // 修改后的属性：默认认为翻译内容不需要额外标记，只有特殊内容才标记为需要
    public bool NeedsLabel { get; set; }
    public List<string> Labels { get; set; } = new List<string>();
}

public class RegexRule {
    public string label { get; set; } = string.Empty;
    public string regex_base64 { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    // 编译后的正则表达式
    public Regex? CompiledRegex {
        get {
            try {
                string pattern = Encoding.UTF8.GetString(Convert.FromBase64String(regex_base64));
                return new Regex(pattern);
            }
            catch {
                return null;
            }
        }
    }
}

class Program {
    static void Main(string[] args) {
        string dllPath = null;
        // 查找参数 "-dll" 后面的路径
        for (int i = 0; i < args.Length; i++) {
            if (args[i].Equals("-dll", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 < args.Length) {
                    dllPath = args[i + 1];
                }
                break;
            }
        }
        if (string.IsNullOrEmpty(dllPath)) {
            Console.WriteLine("Usage: ExtractStrings -dll <path_to_Assembly-CSharp.dll>");
            return;
        }
        
        // 加载目标程序集
        ModuleDefMD module = ModuleDefMD.Load(dllPath);

        // 保存常规提取的字符串（键值去重）
        Dictionary<string, ExtractedString> extractedStrings = new Dictionary<string, ExtractedString>();
        // 保存用于字符串比较的字符串
        Dictionary<string, ExtractedString> comparisonStrings = new Dictionary<string, ExtractedString>();

        // 遍历所有类型和方法
        foreach (var type in module.Types) {
            foreach (var method in type.Methods) {
                if (!method.HasBody || method.Body?.Instructions == null)
                    continue;
                var instructions = method.Body.Instructions;
                for (int i = 0; i < instructions.Count; i++) {
                    var instr = instructions[i];
                    
                    // 提取所有 ldstr 指令的字符串
                    if (instr.OpCode.Code == Code.Ldstr && instr.Operand is string text && !string.IsNullOrEmpty(text)) {
                        string context = method.FullName;
                        string key = GenerateKey(context, text);
                        if (!extractedStrings.ContainsKey(key)) {
                            // 默认情况下，翻译文本不需要额外标记
                            bool needsLabel = false;
                            extractedStrings.Add(key, new ExtractedString {
                                Key = key,
                                OriginalText = text,
                                Context = context,
                                NeedsLabel = needsLabel
                            });
                        }
                    }
                    
                    // 检测用于字符串比较的字符串（通过 IL 分析自动标记，需要额外标记）
                    if ((instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt)
                        && instr.Operand is IMethod calledMethod) {
                        string calledFullName = calledMethod.FullName;
                        if (calledFullName.Contains("System.String::op_Equality") || 
                            calledFullName.Contains("System.String::Equals")) {
                            // 检查前面最多两条指令，寻找 ldstr
                            int start = Math.Max(i - 2, 0);
                            for (int j = start; j < i; j++) {
                                var prevInstr = instructions[j];
                                if (prevInstr.OpCode.Code == Code.Ldstr && prevInstr.Operand is string cmpText && !string.IsNullOrEmpty(cmpText)) {
                                    string cmpContext = $"{method.FullName} [用于字符串比较]";
                                    string cmpKey = GenerateKey(cmpContext, cmpText);
                                    if (!comparisonStrings.ContainsKey(cmpKey)) {
                                        comparisonStrings.Add(cmpKey, new ExtractedString {
                                            Key = cmpKey,
                                            OriginalText = cmpText,
                                            Context = cmpContext,
                                            // 对于用于比较的字符串，需要额外标记以便后续特殊处理
                                            NeedsLabel = true
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // 合并两组记录（注意它们的 Key 通常不同）
        List<ExtractedString> allRecords = extractedStrings.Values
            .Concat(comparisonStrings.Values)
            .ToList();

        // 生成 CSV 文件
        WriteCsv("sddt_0.00_la_Assembly-CSharp.csv", allRecords, comparisonStrings);
    }

    // 写入 CSV 文件的方法
    static void WriteCsv(string fileName, List<ExtractedString> records, Dictionary<string, ExtractedString> comparisonDict) {
        try {
            // 加载外部 JSON 中配置的正则规则
            List<RegexRule> regexRules = new List<RegexRule>();
            if (File.Exists("regex_base64.json")) {
                string json = File.ReadAllText("regex_base64.json");
                regexRules = JsonSerializer.Deserialize<List<RegexRule>>(json) ?? new List<RegexRule>();
            }

            StringBuilder sb = new StringBuilder();
            // CSV 头部（所有字段均加双引号）
            sb.AppendLine($"{EscapeCsv("Key")},{EscapeCsv("Source_string")},{EscapeCsv("Translation")},{EscapeCsv("Context")},{EscapeCsv("Labels")}");
            foreach (var record in records) {
                // 默认翻译内容为空
                string translation = "";
                List<string> labels = new List<string>();

                // 如果该字符串用于比较，则标记 compare_str
                if (comparisonDict.ContainsKey(record.Key)) {
                    labels.Add("compare_str");
                }

                // 应用所有外部 JSON 中配置的正则规则
                foreach (var rule in regexRules) {
                    var compiled = rule.CompiledRegex;
                    if (compiled != null && compiled.IsMatch(record.OriginalText)) {
                        if (!labels.Contains(rule.label))
                            labels.Add(rule.label);
                    }
                }
                
                string labelsStr = string.Join(";", labels);
                
                string line = string.Format("{0},{1},{2},{3},{4}",
                    EscapeCsv(record.Key),
                    EscapeCsv(record.OriginalText),
                    EscapeCsv(translation),
                    EscapeCsv(record.Context),
                    EscapeCsv(labelsStr)
                );
                sb.AppendLine(line);
            }
            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"{fileName} 已生成。");
        }
        catch (Exception ex) {
            Console.WriteLine($"写入 {fileName} 时出错：{ex.Message}");
        }
    }

    // CSV 字段转义：无论字段内容如何，都加双引号，内部双引号替换为两个双引号
    static string EscapeCsv(string field) {
        if (field == null)
            field = "";
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    // 生成稳定唯一的 Key（使用方法全名和字符串内容生成 SHA1 哈希）
    static string GenerateKey(string context, string text) {
        string input = $"{context}:{text}";
        using (SHA1 sha1 = SHA1.Create()) {
            byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder sb = new StringBuilder();
            foreach (byte b in hashBytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
