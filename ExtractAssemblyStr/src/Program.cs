using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

public class ExtractedString {
    public string Key { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public bool NeedsTranslation { get; set; }
}

class Program {
    // 定义标记的字符模式（仅允许半角英文、数字、标点、半角空格和全角空格，或者以"//"开头）
    static Regex passthroughRegex = new Regex(@"^(?://.*|[ -~\u3000]+)$");

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
        // 保存用于比较的危险字符串
        Dictionary<string, ExtractedString> dangerousStrings = new Dictionary<string, ExtractedString>();

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
                            bool needsTranslation = !passthroughRegex.IsMatch(text);
                            extractedStrings.Add(key, new ExtractedString {
                                Key = key,
                                OriginalText = text,
                                Context = context,
                                NeedsTranslation = needsTranslation
                            });
                        }
                    }
                    
                    // 检测用于字符串比较的危险字符串
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
                                    if (!dangerousStrings.ContainsKey(cmpKey)) {
                                        dangerousStrings.Add(cmpKey, new ExtractedString {
                                            Key = cmpKey,
                                            OriginalText = cmpText,
                                            Context = cmpContext,
                                            // 用于比较的字符串通常不应翻译
                                            NeedsTranslation = false
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // 合并两组记录（注意它们的Key一般不同）
        List<ExtractedString> allRecords = extractedStrings.Values
            .Concat(dangerousStrings.Values)
            .ToList();

        // 生成 CSV 文件
        WriteCsv("sddt_0.00_la_Assembly-CSharp.csv", allRecords, dangerousStrings);
    }

    // 写入 CSV 文件的方法
    static void WriteCsv(string fileName, List<ExtractedString> records, Dictionary<string, ExtractedString> dangerousDict) {
        try {
            StringBuilder sb = new StringBuilder();
            // CSV 头部
            sb.AppendLine("Key,Source_string,Translation,Context,Labels");
            foreach (var record in records) {
                // 默认翻译内容
                string translation = "";
                // 计算 Labels，根据规则判断
                List<string> labels = new List<string>();

                // 如果字符串只包含换行符和全角空格（'\n' 和 '\u3000'），标记 only_symbol
                if (IsOnlySymbol(record.OriginalText)) {
                    labels.Add("only_symbol");
                }
                // 如果该字符串用于比较，则标记 compare_str
                if (dangerousDict.ContainsKey(record.Key)) {
                    labels.Add("compare_str");
                }
                // 如果匹配 passthroughPattern，则标记 ogk_label
                if (passthroughRegex.IsMatch(record.OriginalText)) {
                    labels.Add("ogk_label");
                }

                string labelsStr = string.Join(";", labels);
                
                // 生成 CSV 行，各字段需转义
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

    // CSV 字段转义：如果包含逗号、换行符或双引号，则加引号，并将双引号转义为两个双引号
    static string EscapeCsv(string field) {
        if (field.Contains(",") || field.Contains("\n") || field.Contains("\"")) {
            field = field.Replace("\"", "\"\"");
            return $"\"{field}\"";
        }
        return field;
    }

    // 生成稳定唯一的 Key，使用方法全名和字符串内容生成 SHA1 哈希
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

    // 判断字符串是否只包含换行符和全角空格（'\n' 和 '\u3000'）
    static bool IsOnlySymbol(string text) {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (char c in text) {
            if (c != '\n' && c != '\u3000')
                return false;
        }
        return true;
    }
}
