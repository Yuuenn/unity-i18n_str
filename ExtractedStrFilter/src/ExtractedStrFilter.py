import csv
import sys
import argparse

def main():
    parser = argparse.ArgumentParser(description="处理 CSV 文件")
    parser.add_argument('-csv', required=True, help="输入 CSV 文件的路径")
    # 使用 nargs='+' 允许传入多个标签参数，标签之间以空格分隔
    parser.add_argument('-labels', nargs='+', help='指定标签筛选，支持多个标签。未指定时，仅筛选 "Labels" 列为空的行', default=None)
    args = parser.parse_args()
        
    input_csv = args.csv
    print("输入的 CSV 文件路径为:", input_csv)
    
    # 根据原文件名生成两个新的文件名
    base_name = input_csv.rsplit('.', 1)[0]
    filtered_csv = base_name + '_filtered.csv'
    excluded_csv = base_name + '_excluded.csv'
    
    with open(input_csv, 'r', encoding='utf-8-sig', newline='') as csv_in:
        reader = csv.DictReader(csv_in)
        fieldnames = reader.fieldnames
        filtered_rows = []
        excluded_rows = []
    
        # 如果未指定 -labels 参数，则默认筛选空字符串的行（等同于 -labels ""）
        if args.labels is None:
            print("未指定筛选标签，默认只筛选 'Labels' 列为空的行。")
            for row in reader:
                if row.get("Labels", "").strip() == "":
                    filtered_rows.append(row)
                else:
                    excluded_rows.append(row)
        else:
            # 这里直接使用传入的多个标签，保留空字符串作为一个有效标签
            target_labels = [label.strip() for label in args.labels]
            print("筛选包含以下标签的行:", target_labels)
            for row in reader:
                labels_field = row.get("Labels", "").strip()
                # 将 "Labels" 字符串以英文分号拆分为列表（过滤掉实际存在的空白项）
                labels_list = [l.strip() for l in labels_field.split(';') if l.strip()]
                # 若目标标签中包含空字符串且行的 labels 为空，或者行中包含任一非空目标标签，则符合条件
                if ("" in target_labels and labels_field == "") or any(target in labels_list for target in target_labels if target != ""):
                    filtered_rows.append(row)
                else:
                    excluded_rows.append(row)
    
    # 将筛选后的行写入新的 CSV 文件，所有值均以字符串形式导出
    with open(filtered_csv, 'w', encoding='utf-8-sig', newline='') as csv_out:
        writer = csv.DictWriter(csv_out, fieldnames=fieldnames, quoting=csv.QUOTE_ALL)
        writer.writeheader()
        writer.writerows(filtered_rows)
    
    # 将被过滤掉的行写入另一个 CSV 文件
    with open(excluded_csv, 'w', encoding='utf-8-sig', newline='') as csv_out:
        writer = csv.DictWriter(csv_out, fieldnames=fieldnames, quoting=csv.QUOTE_ALL)
        writer.writeheader()
        writer.writerows(excluded_rows)
    
    print(f"过滤后的 CSV 文件已保存为 {filtered_csv}。")
    print(f"被过滤掉的 CSV 文件已保存为 {excluded_csv}。")

if __name__ == '__main__':
    main()
