import csv
import sys
import argparse

def main():
    parser = argparse.ArgumentParser(description="处理 CSV 文件")
    parser.add_argument('-csv', required=True, help="输入 CSV 文件的路径")
    args = parser.parse_args()
        
    input_csv = args.csv
    print("输入的 CSV 文件路径为:", input_csv)
    
    # 以原文件名为基础，生成两个新的文件名
    base_name = input_csv.rsplit('.', 1)[0]
    filtered_csv = base_name + '_filtered.csv'
    excluded_csv = base_name + '_excluded.csv'
    
    # 要排除的标签列表
    exclude_labels = ["only_symbol", "compare_str", "ogk_label"]
    
    with open(input_csv, 'r', encoding='utf-8', newline='') as csv_in:
        reader = csv.DictReader(csv_in)
        fieldnames = reader.fieldnames
        filtered_rows = []
        excluded_rows = []
        
        for row in reader:
            labels = row.get("Labels", "")
            # 如果 labels 中包含任一排除标签，则加入 excluded_rows，否则加入 filtered_rows
            if any(ex_label in labels for ex_label in exclude_labels):
                excluded_rows.append(row)
            else:
                filtered_rows.append(row)
    
    # 写入过滤后的行到新的 CSV 文件
    with open(filtered_csv, 'w', encoding='utf-8', newline='') as csv_out:
        writer = csv.DictWriter(csv_out, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(filtered_rows)
    
    # 写入被过滤掉的行到另一个 CSV 文件
    with open(excluded_csv, 'w', encoding='utf-8', newline='') as csv_out:
        writer = csv.DictWriter(csv_out, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(excluded_rows)
    
    print(f"过滤后的 CSV 文件已保存为 {filtered_csv}。")
    print(f"被过滤掉的 CSV 文件已保存为 {excluded_csv}。")

if __name__ == '__main__':
    main()

