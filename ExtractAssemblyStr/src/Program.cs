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
    public string AuxiliaryKey { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    // 默认情况下，从 ldstr 指令提取的字符串不需要额外标记，只有特殊内容才需要标记
    public bool NeedsLabel { get; set; }
    public List<string> Labels { get; set; } = new List<string>();
}

public class RegexRule {
    public string label { get; set; } = string.Empty;
    public string regex_base64 { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    // 编译后的正则表达式
    public Regex CompiledRegex {
        get {
            try {
                string pattern = Encoding.UTF8.GetString(Convert.FromBase64String(regex_base64));
                return new Regex(pattern);
            }
            catch {
                return null; // 返回 null 表示编译失败
            }
        }
    }
}

class Program {
    static void Main(string[] args) {
        string dllPath = null;
        string regexPath = "regex_base64.json"; // 默认 JSON 文件名
        bool enableOngeki = false;             // 是否启用“Scene\d+IDEnum”匹配

        // 遍历命令行参数
        for (int i = 0; i < args.Length; i++) {
            if (args[i].Equals("-dll", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 < args.Length) {
                    dllPath = args[i + 1];
                }
            }
            else if (args[i].Equals("-regex", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 < args.Length) {
                    regexPath = args[i + 1];
                }
            }
            else if (args[i].Equals("-ongeki", StringComparison.OrdinalIgnoreCase)) {
                enableOngeki = true;
            }
        }

        if (string.IsNullOrEmpty(dllPath)) {
            Console.WriteLine("Usage: ExtractStrings -dll <path_to_Assembly-CSharp.dll> [-regex <path_to_regex_json>] [-ongeki]");
            return;
        }
        
        // 加载目标程序集
        ModuleDefMD module = ModuleDefMD.Load(dllPath);

        // 保存常规提取的字符串（键值去重）
        Dictionary<string, ExtractedString> extractedStrings = new Dictionary<string, ExtractedString>();
        // 保存用于字符串比较的字符串（更科学的名称：comparisonStrings）
        Dictionary<string, ExtractedString> comparisonStrings = new Dictionary<string, ExtractedString>();

        // 如果开启了 -ongeki 参数，就准备一个正则表达式，用来匹配 Scene\d+IDEnum
        Regex sceneEnumRegex = null;
        if (enableOngeki) {
            sceneEnumRegex = new Regex(@"^Scene\d+IDEnum$", RegexOptions.Compiled);
        }

        // 遍历所有类型和方法
        foreach (var type in module.Types) {
            bool isSceneEnum = false;
            if (enableOngeki && sceneEnumRegex != null) {
                // 检查类型名是否匹配 Scene\d+IDEnum
                isSceneEnum = sceneEnumRegex.IsMatch(type.Name);
            }

            foreach (var method in type.Methods) {
                if (!method.HasBody || method.Body?.Instructions == null)
                    continue;
                var instructions = method.Body.Instructions;
                // 为每个方法维护 ldstr 指令的序号计数器和映射表
                int ldstrIndex = 0;
                Dictionary<Instruction, string> ldstrKeyMap = new Dictionary<Instruction, string>();

                for (int i = 0; i < instructions.Count; i++) {
                    var instr = instructions[i];
                    
                    // 提取所有 ldstr 指令的字符串
                    if (instr.OpCode.Code == Code.Ldstr && instr.Operand is string text && !string.IsNullOrEmpty(text)) {
                        // 先标准化换行符
                        string normalizedText = NormalizeNewlines(text);
                        // 使用新逻辑生成主 Key 和辅助 Key
                        string primaryKey = KeyGenerator.GeneratePrimaryKey(type.Name, method.Name, ldstrIndex);
                        string auxiliaryKey = KeyGenerator.GenerateAuxiliaryKey(normalizedText);
                        ldstrKeyMap[instr] = primaryKey;
                        ldstrIndex++;

                        string context = method.FullName;
                        if (!extractedStrings.ContainsKey(primaryKey)) {
                            // 默认情况下，不需要额外标记
                            bool needsLabel = false;
                            var newRecord = new ExtractedString {
                                Key = primaryKey,
                                AuxiliaryKey = auxiliaryKey,
                                OriginalText = normalizedText,
                                Context = context,
                                NeedsLabel = needsLabel
                            };

                            // 如果是 -ongeki 模式，并且类型名匹配 scene\d+IDEnum，就加上标签
                            if (isSceneEnum) {
                                newRecord.Labels.Add("from_scene_idenum");
                            }

                            extractedStrings.Add(primaryKey, newRecord);
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
                                    // 标准化换行符
                                    string normalizedCmpText = NormalizeNewlines(cmpText);
                                    // 尝试从映射表中获取主 Key
                                    string cmpKey = "";
                                    if (!ldstrKeyMap.TryGetValue(prevInstr, out cmpKey)) {
                                        // 若未记录，则生成一个新的主 Key
                                        cmpKey = KeyGenerator.GeneratePrimaryKey(type.Name, method.Name, ldstrIndex);
                                        ldstrKeyMap[prevInstr] = cmpKey;
                                        ldstrIndex++;
                                    }
                                    string cmpAuxiliaryKey = KeyGenerator.GenerateAuxiliaryKey(normalizedCmpText);
                                    string cmpContext = $"{method.FullName} [comparing strings]";
                                    if (!comparisonStrings.ContainsKey(cmpKey)) {
                                        comparisonStrings.Add(cmpKey, new ExtractedString {
                                            Key = cmpKey,
                                            AuxiliaryKey = cmpAuxiliaryKey,
                                            OriginalText = normalizedCmpText,
                                            Context = cmpContext,
                                            // 对于用于比较的字符串，需要额外标记
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

        // 合并所有记录
        List<ExtractedString> allRecords = extractedStrings.Values
            .Concat(comparisonStrings.Values)
            .ToList();

        // 去重时打印日志，输出重复的 Key 及重复记录的 Context 信息
        Dictionary<string, ExtractedString> uniqueRecords = new Dictionary<string, ExtractedString>();
        foreach (var record in allRecords) {
            if (uniqueRecords.ContainsKey(record.Key)) {
                Console.WriteLine($"去重：发现重复 Key: {record.Key}. 原始 Context: {uniqueRecords[record.Key].Context}，重复 Context: {record.Context}");
            } else {
                uniqueRecords.Add(record.Key, record);
            }
        }
        List<ExtractedString> finalRecords = uniqueRecords.Values.ToList();

        // 生成 CSV 文件
        WriteCsv("sddt_0.00_la_Assembly-CSharp.csv", finalRecords, comparisonStrings, regexPath);
    }

    // 新增：用于标准化换行符的方法，将文本中的换行符转换为 "\n"
    static string NormalizeNewlines(string text) {
        if (string.IsNullOrEmpty(text))
            return text;
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    // 写入 CSV 文件的方法，增加参数 regexPath 用于加载外部 JSON 文件
    static void WriteCsv(string fileName, List<ExtractedString> records, Dictionary<string, ExtractedString> comparisonDict, string regexPath) {
        try {
            // 加载外部 JSON 中配置的正则规则
            List<RegexRule> regexRules = new List<RegexRule>();
            if (File.Exists(regexPath)) {
                string json = File.ReadAllText(regexPath);
                regexRules = JsonSerializer.Deserialize<List<RegexRule>>(json) ?? new List<RegexRule>();
            } else {
                Console.WriteLine($"未找到正则规则文件: {regexPath}");
            }

            StringBuilder sb = new StringBuilder();
            // CSV 头部（所有字段均加双引号），新增辅助 Key 列
            sb.AppendLine($"{EscapeCsv("Key")},{EscapeCsv("Source_string")},{EscapeCsv("Translation")},{EscapeCsv("Context")},{EscapeCsv("Labels")},{EscapeCsv("AuxiliaryKey")}");
            foreach (var record in records) {
                // 默认翻译内容为空
                string translation = "";
                List<string> labels = new List<string>(record.Labels);

                // 如果该字符串用于比较，则标记 compare_str
                if (comparisonDict.ContainsKey(record.Key)) {
                    labels.Add("compare_str");
                }

                // 应用所有外部 JSON 中配置的正则规则
                foreach (var rule in regexRules) {
                    Regex compiled = rule.CompiledRegex;
                    if (compiled != null && compiled.IsMatch(record.OriginalText)) {
                        if (!labels.Contains(rule.label))
                            labels.Add(rule.label);
                    }
                }
                
                string labelsStr = string.Join(";", labels);
                
                string line = string.Format("{0},{1},{2},{3},{4},{5}",
                    EscapeCsv(record.Key),
                    EscapeCsv(record.OriginalText),
                    EscapeCsv(translation),
                    EscapeCsv(record.Context),
                    EscapeCsv(labelsStr),
                    EscapeCsv(record.AuxiliaryKey)
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

    public static class KeyGenerator {
        /// <summary>
        /// 生成 主 Key（类型 + 方法 + 字符串序号）
        /// </summary>
        public static string GeneratePrimaryKey(string className, string methodName, int index) {
            return $"{className}::{methodName}:{index}";
        }

        /// <summary>
        /// 生成 辅助 Key（原始字符串的 SHA1 哈希）
        /// </summary>
        public static string GenerateAuxiliaryKey(string text) {
            using (SHA1 sha1 = SHA1.Create()) {
                byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
