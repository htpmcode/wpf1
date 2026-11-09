using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace viewfile
{
    public partial class MainWindow : Window
    {
        // 用于绑定到 DataGrid 的集合
        private List<FileDisplayItem> _currentFiles = new List<FileDisplayItem>();
        // 文件说明字典：文件名（不含路径） -> 说明内容
        private Dictionary<string, string> _commentDict =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MainWindow()
        {
            InitializeComponent();
            UpdateStatus("就绪。请选择文件夹后点击\"扫描并关联\"。");
        }

        /// <summary>
        /// 浏览按钮：弹出文件夹选择对话框，选择后自动扫描
        /// </summary>
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "请选择要扫描的文件夹",
                InitialDirectory = Directory.Exists(TxtFolderPath.Text.Trim())
                                   ? TxtFolderPath.Text.Trim()
                                   : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtFolderPath.Text = dialog.FolderName;
                BtnScan_Click(sender, e); // 选择后自动扫描
            }
        }

        /// <summary>
        /// 扫描文件夹中的文件，并关联 zhushi.txt 中的说明
        /// </summary>
        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            string folder = TxtFolderPath.Text.Trim();
            if (!Directory.Exists(folder))
            {
                MessageBox.Show("文件夹不存在，请检查路径。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateStatus("扫描失败：文件夹不存在。");
                return;
            }

            try
            {
                // 1. 读取 zhushi.txt 并建立映射
                int commentCount = LoadComments(folder);

                // 2. 解析过滤模式（分号分隔）
                var patterns = TxtFilter.Text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(p => p.Trim())
                                             .Where(p => !string.IsNullOrEmpty(p))
                                             .ToArray();
                if (patterns.Length == 0) patterns = new[] { "*.*" };

                // 3. 扫描文件（根据复选框决定是否递归）
                var searchOption = ChkRecursive.IsChecked == true
                                   ? SearchOption.AllDirectories
                                   : SearchOption.TopDirectoryOnly;
                var files = new List<FileInfo>();
                foreach (var pattern in patterns)
                {
                    try
                    {
                        files.AddRange(new DirectoryInfo(folder).GetFiles(pattern, searchOption));
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"读取模式 {pattern} 时出错：{ex.Message}");
                    }
                }

                // 去重 + 排序
                var distinctFiles = files.GroupBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                                         .Select(g => g.First())
                                         .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                                         .ToList();

                // 4. 构建显示对象
                _currentFiles = distinctFiles.Select(f => new FileDisplayItem
                {
                    Name = f.Name,
                    SizeKB = Math.Round(f.Length / 1024.0, 1),
                    FullPath = f.FullName,
                    HasCommentText = _commentDict.ContainsKey(f.Name) ? "有" : "无",
                    Comment = _commentDict.TryGetValue(f.Name, out var c)
                              ? c
                              : "（zhushi.txt 中未找到该文件的说明）"
                }).ToList();

                FileGrid.ItemsSource = _currentFiles;
                TxtComment.Text = "（请选择左侧文件查看说明）";
                TxtSelectedFileName.Text = "";
                TxtFileCount.Text = $"共 {_currentFiles.Count} 个文件";

                UpdateStatus($"扫描完成：共 {_currentFiles.Count} 个文件，zhushi.txt 解析出 {commentCount} 条说明。");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"扫描出错：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus($"扫描出错：{ex.Message}");
            }
        }

        /// <summary>
        /// 读取文件夹下的 zhushi.txt，使用正则表达式无视空格、制表符及分隔线，精准提取说明。
        /// </summary>
        private int LoadComments(string folder)
        {
            _commentDict.Clear();
            string commentPath = Path.Combine(folder, "zhushi.txt");
            if (!File.Exists(commentPath))
                return 0;

            int count = 0;
            try
            {
                // 核心正则：匹配 "任意文件名(带扩展名)" + "至少一个空格/制表符" + "剩余说明"
                // 例如 "bun.exe   --js的运行时" 能精确拆分为 bun.exe 和 --js的运行时
                var regex = new Regex(
                    @"^\s*(?<file>[^\s=:]+?\.\w+)\s+(?<comment>.*)$",
                    RegexOptions.Compiled);

                foreach (var rawLine in File.ReadAllLines(commentPath))
                {
                    string line = rawLine.Trim();
                    // 跳过空行、以 # 开头的注释行
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    // 跳过纯修饰线（===== 或 -----）
                    if (line.StartsWith("=") || line.StartsWith("-"))
                        continue;

                    Match match = regex.Match(line);
                    if (match.Success)
                    {
                        string fileName = match.Groups["file"].Value.Trim().Trim('"', '\'');
                        string comment = match.Groups["comment"].Value.Trim();

                        if (!string.IsNullOrEmpty(fileName) && !_commentDict.ContainsKey(fileName))
                        {
                            _commentDict[fileName] = comment;
                            count++;
                        }
                    }
                    else
                    {
                        // 兜底：尝试用 '=' 或 ':' 分隔的格式
                        int idx = line.IndexOfAny(new[] { '=', ':' });
                        if (idx > 0)
                        {
                            string fileName = line.Substring(0, idx).Trim().Trim('"', '\'');
                            string comment = line.Substring(idx + 1).Trim();
                            if (!string.IsNullOrEmpty(fileName) && !_commentDict.ContainsKey(fileName))
                            {
                                _commentDict[fileName] = comment;
                                count++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 zhushi.txt 失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return count;
        }

        /// <summary>
        /// 选中文件后，在右侧展示文件名与对应说明
        /// </summary>
        private void FileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileGrid.SelectedItem is FileDisplayItem item)
            {
                TxtSelectedFileName.Text = item.FullPath;
                TxtComment.Text = item.Comment;
            }
        }

        private void UpdateStatus(string message)
        {
            TxtStatus.Text = message;
        }
    }

    /// <summary>
    /// 用于 DataGrid 绑定的显示模型
    /// </summary>
    public class FileDisplayItem
    {
        public string Name { get; set; } = "";
        public double SizeKB { get; set; }
        public string FullPath { get; set; } = "";
        public string HasCommentText { get; set; } = "";  // "有" / "无"
        public string Comment { get; set; } = "";          // 说明正文
    }
}