using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using CsvHelper;
using CsvHelper.Configuration;

namespace AssemblyStringReplacerApp
{
    /// <summary>
    /// 简单的日志记录器，将日志同时写入控制台和文件。
    /// </summary>
    public static class Logger
    {
        private static readonly string logFile = "AssemblyStringReplacer.log";

        public static void Log(string message)
        {
            string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
            Console.WriteLine(message);
            try
            {
                File.AppendAllText(logFile, logLine + Environment.NewLine);
            }
            catch
            {
                // 忽略日志写入错误
            }
        }
    }

    // 用于映射 CSV 中的每一条记录
    public class CsvTranslationRecord
    {
        public string Key { get; set; }
        public string Source_string { get; set; }
        public string Translation { get; set; }
        public string Context { get; set; }
        public string Labels { get; set; }
    }

    public class AssemblyStringReplacer
    {
        // 翻译字典，键为生成的唯一 Key，值为翻译后的文本
        public Dictionary<string, string> Translations { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// 从 CSV 文件中加载翻译数据。要求 CSV 文件为 UTF-8 格式，
        /// 且包含列：Key, Source_string, Translation, Context, Labels。
        /// 仅当 Translation 去除空白后非空时加载数据。
        /// </summary>
        /// <param name="csvPath">翻译数据 CSV 文件路径</param>
        public void LoadTranslations(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                Logger.Log($"未找到翻译文件：{csvPath}");
                return;
            }
            try
            {
                // 使用 UTF-8 编码（带 BOM）读取 CSV 文件
                using (var reader = new StreamReader(csvPath, new UTF8Encoding(true)))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null,
                    BadDataFound = null,
                }))
                {
                    var records = csv.GetRecords<CsvTranslationRecord>();
                    int count = 0;
                    foreach (var record in records)
                    {
                        // 仅当 Translation 去除空白后非空时加入翻译字典
                        if (!string.IsNullOrWhiteSpace(record.Translation))
                        {
                            if (!Translations.ContainsKey(record.Key))
                            {
                                Translations.Add(record.Key, record.Translation.Trim());
                                count++;
                            }
                        }
                    }
                    Logger.Log($"加载了 {count} 条翻译数据。");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"加载翻译数据时出错：{ex}");
            }
        }

        /// <summary>
        /// 根据方法全名和字符串内容生成一个唯一的 Key（使用 SHA1 哈希）。
        /// Key 格式为 SHA1(方法全名 + ":" + 原始字符串)。
        /// </summary>
        public string GenerateKey(string methodFullName, string text)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(methodFullName + ":" + text);
                byte[] hashBytes = sha1.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// 遍历程序集中的所有类型和方法，将其中的 ldstr 指令替换为翻译后的文本（如果存在匹配的 Key）。
        /// </summary>
        /// <param name="inputPath">原始程序集路径</param>
        /// <param name="outputPath">输出修改后的程序集路径</param>
        public void ProcessAssembly(string inputPath, string outputPath)
        {
            if (!File.Exists(inputPath))
            {
                Logger.Log($"未找到输入程序集：{inputPath}");
                return;
            }
            try
            {
                ModuleDefMD module = ModuleDefMD.Load(inputPath);
                foreach (var type in module.Types)
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody || method.Body?.Instructions == null)
                            continue;
                        foreach (var instr in method.Body.Instructions)
                        {
                            if (instr.OpCode.Code == Code.Ldstr && instr.Operand is string originalString)
                            {
                                string methodFullName = method.FullName;
                                string key = GenerateKey(methodFullName, originalString);
                                // 仅在翻译字典中存在对应的非空翻译时替换
                                if (Translations.TryGetValue(key, out string translated))
                                {
                                    Logger.Log($"替换 {methodFullName} 中的字符串：\"{originalString}\" -> \"{translated}\" (Key: {key})");
                                    instr.Operand = translated;
                                }
                            }
                        }
                    }
                }
                module.Write(outputPath);
                Logger.Log($"处理完毕，生成新程序集：{outputPath}");
            }
            catch (Exception ex)
            {
                Logger.Log($"处理程序集时出错：{ex}");
            }
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            // 解析命令行参数
            string csvPath = null;
            string inputDllPath = null;
            string outputDllPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-csv":
                        if (i + 1 < args.Length)
                        {
                            csvPath = args[++i];
                        }
                        break;
                    case "-inputdll":
                        if (i + 1 < args.Length)
                        {
                            inputDllPath = args[++i];
                        }
                        break;
                    case "-outputdll":
                        if (i + 1 < args.Length)
                        {
                            outputDllPath = args[++i];
                        }
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(csvPath) || string.IsNullOrWhiteSpace(inputDllPath) || string.IsNullOrWhiteSpace(outputDllPath))
            {
                Logger.Log("Usage: AssemblyStringReplacer -inputdll <inputAssembly.dll> -outputdll <outputAssembly.dll> -csv <translationData.csv>");
                return;
            }

            AssemblyStringReplacer replacer = new AssemblyStringReplacer();
            Logger.Log("开始加载翻译数据……");
            replacer.LoadTranslations(csvPath);
            Logger.Log("开始处理程序集……");
            replacer.ProcessAssembly(inputDllPath, outputDllPath);
        }
    }
}

