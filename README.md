# unity-i18n_str

一个面向 Unity `Assembly-CSharp.dll` 的字符串国际化辅助工具仓库，主要用于从程序集里提取 `ldstr` 字面量、按规则筛选待翻译文本，并将翻译结果回写到程序集。

这个仓库更适合以下场景：

- 需要对已编译的 Unity 程序集进行文本提取与汉化
- 希望把程序集中的字符串整理成 CSV，交给翻译或批量处理
- 需要先排除路径、数字、注释、空白等明显不需要翻译的内容

## 仓库结构

- `ExtractAssemblyStr/src`
  - C# 提取工具
  - 遍历 `Assembly-CSharp.dll` 中的方法体，提取 `ldstr` 指令里的字符串
  - 输出带上下文、标签和辅助 Key 的 CSV 文件
- `ExtractedStrFilter/src`
  - Python 过滤工具
  - 按 `Labels` 列拆分 CSV，筛出需要处理或需要排除的文本
- `AssemblyStrReplacer/src`
  - C# 回写工具
  - 读取翻译 CSV，把翻译后的字符串替换回程序集中的 `ldstr`

## 典型工作流

1. 使用 `ExtractAssemblyStr` 从目标程序集提取字符串。
2. 根据正则规则自动打标签，或用 `ExtractedStrFilter` 过滤掉不需要翻译的条目。
3. 在 CSV 的 `Translation` 列填写译文。
4. 使用 `AssemblyStrReplacer` 将译文回写到新的程序集文件。

## 各工具说明

### 1. ExtractAssemblyStr

用途：

- 扫描 Unity 程序集中的字符串常量
- 记录字符串来源方法，方便定位上下文
- 为特定模式自动打标签
- 额外识别用于字符串比较的文本，并标记为 `compare_str`

输出 CSV 列：

- `Key`：主键
- `Source_string`：原始字符串
- `Translation`：预留译文列
- `Context`：方法上下文
- `Labels`：自动标签
- `AuxiliaryKey`：原文内容的 SHA1 辅助键

命令示例：

```powershell
dotnet run --project .\ExtractAssemblyStr\src\ExtractAssemblyStr.csproj -- -dll "C:\path\to\Assembly-CSharp.dll"
```

可选参数：

- `-regex <path>`：指定标签规则 JSON，默认使用 `regex_base64.json`
- `-ongeki`：启用 `Scene\d+IDEnum` 相关特殊标记逻辑

默认正则规则会给如下内容打标签：

- 空字符串、仅空格、仅换行
- 数字
- 带数字的字母数字串
- 路径样式文本
- 注释行
- 特定语义短语
- 一些明显不适合直接翻译的 ASCII / 正则样式内容

### 2. ExtractedStrFilter

用途：

- 根据 `Labels` 列把 CSV 拆成两份
- 一份是符合筛选条件的记录，另一份是被排除的记录

命令示例：

```powershell
python .\ExtractedStrFilter\src\ExtractedStrFilter.py -csv ".\sddt_0.00_la_Assembly-CSharp.csv"
```

默认行为：

- 不传 `-labels` 时，只保留 `Labels` 为空的记录

按标签筛选示例：

```powershell
python .\ExtractedStrFilter\src\ExtractedStrFilter.py -csv ".\sddt_0.00_la_Assembly-CSharp.csv" -labels compare_str from_scene_idenum
```

输出文件：

- `*_filtered.csv`
- `*_excluded.csv`

### 3. AssemblyStrReplacer

用途：

- 读取带译文的 CSV
- 遍历程序集中的 `ldstr`
- 将已填写的 `Translation` 回写到新 DLL

命令示例：

```powershell
dotnet run --project .\AssemblyStrReplacer\src\AssemblyStrReplacer.csproj -- -inputdll "C:\path\to\Assembly-CSharp.dll" -outputdll "C:\path\to\Assembly-CSharp.i18n.dll" -csv "C:\path\to\translation.csv"
```

## 依赖环境

- `.NET Framework 4.7.2` / 可构建 `net472` 项目的 .NET SDK
- Python 3
- NuGet 包：
  - `dnlib`
  - `CsvHelper`
  - `System.Text.Json`

## 注意事项

- 仓库当前实现的整体目标很明确：服务于 Unity 程序集文本提取与汉化回写。
- 目前 `ExtractAssemblyStr` 生成的 `Key` 格式是 `类名::方法名:序号`，而 `AssemblyStrReplacer` 回写时使用的是基于 `方法全名 + 原文` 的 SHA1 Key。
- 这意味着提取与回写在现状下存在 Key 规则不一致的问题；如果要直接串联使用，建议先统一两个工具的 Key 生成逻辑。
- 回写工具会直接修改输出程序集中的字符串常量，正式使用前建议先备份原始 DLL。

## 适合写在 GitHub 的中文仓库描述

可直接使用这一句作为仓库描述：

`用于 Unity Assembly-CSharp.dll 的字符串提取、标签过滤与翻译回写的国际化辅助工具集。`
