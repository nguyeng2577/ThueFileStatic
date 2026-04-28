using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Data.Sqlite;

namespace AppLocDuLieuThue
{
    public partial class Form1 : Form
    {
        string dbConnectionString = "Data Source=DuLieuThue2026.sqlite;";

        // Biến đếm an toàn trong đa luồng
        int fileThanhCong = 0;
        int fileBoQua = 0;

        // Cấu trúc dữ liệu để truyền qua lại giữa các Luồng
        public class ParsedRow
        {
            public string MaSoThueThuMuc { get; set; }
            public string TenFile { get; set; }
            public Dictionary<string, string> Data { get; set; } = new Dictionary<string, string>();
        }

        public Form1()
        {
            InitializeComponent();
            if (this.btnChonThuMuc != null)
            {
                this.btnChonThuMuc.Click -= btnChonThuMuc_Click;
                this.btnChonThuMuc.Click += new System.EventHandler(this.btnChonThuMuc_Click);
            }
            if (this.btnBatDau != null)
            {
                this.btnBatDau.Click -= btnBatDau_Click;
                this.btnBatDau.Click += new System.EventHandler(this.btnBatDau_Click);
            }
        }

        private void btnChonThuMuc_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Hãy chọn thư mục gốc chứa các file dữ liệu (HTML, XML)";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtDuongDan.Text = fbd.SelectedPath;
                    Log("Đã chọn thư mục: " + fbd.SelectedPath);
                }
            }
        }

        private async void btnBatDau_Click(object sender, EventArgs e)
        {
            string thuMucDuLieu = txtDuongDan.Text;

            if (string.IsNullOrWhiteSpace(thuMucDuLieu) || !Directory.Exists(thuMucDuLieu))
            {
                MessageBox.Show("Vui lòng chọn thư mục hợp lệ!", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Log("Bắt đầu xử lý ĐA LUỒNG. Tối ưu 80% CPU...");
            btnBatDau.Enabled = false;

            try
            {
                // Reset biến đếm
                fileThanhCong = 0;
                fileBoQua = 0;
                await Task.Run(() => ProcessAllFilesMultiThreaded(thuMucDuLieu));
            }
            catch (Exception ex)
            {
                Log("LỖI NGHIÊM TRỌNG: " + ex.Message);
                MessageBox.Show("Có lỗi xảy ra: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnBatDau.Enabled = true;
            }
        }

        // --- HÀM XỬ LÝ ĐA LUỒNG LÕI ---
        private void ProcessAllFilesMultiThreaded(string rootFolderPath)
        {
            string[] allFiles = Directory.GetFiles(rootFolderPath, "*.*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Log($"Tìm thấy {allFiles.Length} file. Đang khởi động dàn Parser...");

            // Khởi tạo Băng chuyền dữ liệu an toàn
            using (BlockingCollection<List<ParsedRow>> dataQueue = new BlockingCollection<List<ParsedRow>>(1000))
            {
                // 1. CHẠY LUỒNG GHI (CONSUMER) - Chỉ 1 luồng duy nhất để bảo vệ SQLite
                Task writerTask = Task.Run(() => DatabaseWriterTask(dataQueue));

                // 2. CHẠY LUỒNG ĐỌC (PRODUCER) - Ép dùng tối đa 80% số lõi CPU
                int maxThreads = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));
                ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };

                Parallel.ForEach(allFiles, options, filePath =>
                {
                    try
                    {
                        List<ParsedRow> extractedData = null;
                        if (filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        {
                            extractedData = ParseHtmlFile(filePath);
                        }
                        else if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        {
                            extractedData = ParseXmlFile(filePath);
                        }

                        if (extractedData != null && extractedData.Count > 0)
                        {
                            dataQueue.Add(extractedData); // Ném lên băng chuyền
                            Interlocked.Increment(ref fileThanhCong); // Cộng đếm an toàn
                        }
                        else
                        {
                            Interlocked.Increment(ref fileBoQua);
                        }
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref fileBoQua);
                    }
                });

                // Báo hiệu đã đọc xong toàn bộ file
                dataQueue.CompleteAdding();

                // Đợi luồng ghi ghi nốt những dữ liệu cuối cùng trên băng chuyền
                writerTask.Wait();
            }

            Log($"HOÀN TẤT! Xử lý thành công {fileThanhCong} file. Bỏ qua/Lỗi {fileBoQua} file.");
            this.Invoke(new Action(() =>
            {
                MessageBox.Show($"Xử lý ĐA LUỒNG xong!\n- Thành công: {fileThanhCong}\n- Bỏ qua: {fileBoQua}", "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
        }

        // --- LUỒNG DUY NHẤT LÀM VIỆC VỚI SQLITE (CONSUMER) ---
        private void DatabaseWriterTask(BlockingCollection<List<ParsedRow>> queue)
        {
            using (SqliteConnection conn = new SqliteConnection(dbConnectionString))
            {
                conn.Open();
                string createTableQuery = @"CREATE TABLE IF NOT EXISTS ThongTinToKhai (Id INTEGER PRIMARY KEY AUTOINCREMENT, MaSoThue_ThuMuc TEXT, TenFile TEXT)";
                using (var cmd = new SqliteCommand(createTableQuery, conn)) { cmd.ExecuteNonQuery(); }

                List<string> existingDbColumns = GetExistingColumns(conn, "ThongTinToKhai");

                // Liên tục lấy dữ liệu từ băng chuyền cho đến khi hết
                foreach (List<ParsedRow> fileData in queue.GetConsumingEnumerable())
                {
                    using (var transaction = conn.BeginTransaction())
                    {
                        foreach (var row in fileData)
                        {
                            // 1. Kiểm tra và tạo cột động nếu chưa có
                            foreach (var colName in row.Data.Keys)
                            {
                                if (!existingDbColumns.Exists(c => c.Equals(colName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    string addColQuery = $"ALTER TABLE ThongTinToKhai ADD COLUMN {colName} TEXT;";
                                    using (var cmd = new SqliteCommand(addColQuery, conn, transaction)) { cmd.ExecuteNonQuery(); }
                                    existingDbColumns.Add(colName);
                                    Log($"  -> Đã tạo cột mới: {colName}");
                                }
                            }

                            // 2. Chèn dữ liệu
                            List<string> colNames = new List<string>(row.Data.Keys);
                            List<string> paramNames = new List<string>();
                            for (int i = 0; i < colNames.Count; i++) paramNames.Add("@p" + i);

                            string colNamesJoined = string.Join(", ", colNames.ToArray());
                            string paramNamesJoined = string.Join(", ", paramNames.ToArray());

                            string insertQuery = $@"
                                INSERT INTO ThongTinToKhai (MaSoThue_ThuMuc, TenFile, {colNamesJoined}) 
                                VALUES (@MaSoThueTM, @TenFile, {paramNamesJoined})";

                            using (var cmd = new SqliteCommand(insertQuery, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@MaSoThueTM", row.MaSoThueThuMuc);
                                cmd.Parameters.AddWithValue("@TenFile", row.TenFile);
                                for (int i = 0; i < colNames.Count; i++)
                                {
                                    cmd.Parameters.AddWithValue("@p" + i, row.Data[colNames[i]]);
                                }
                                cmd.ExecuteNonQuery();
                            }
                        }
                        transaction.Commit();
                    }
                }
            }
        }

        // --- HÀM BÓC TÁCH HTML (CHỈ ĐỌC RAW, KHÔNG ĐỤNG DB) ---
        private List<ParsedRow> ParseHtmlFile(string filePath)
        {
            string htmlContent = File.ReadAllText(filePath, Encoding.UTF8);
            if (htmlContent.Contains("Không có dữ liệu thỏa mãn")) return null;

            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.LoadHtml(htmlContent);

            var table = htmlDoc.DocumentNode.SelectSingleNode("//table[contains(@class, 'table-bordered')]") ?? htmlDoc.DocumentNode.SelectSingleNode("//table");
            if (table == null) return null;

            var headerRow = table.SelectSingleNode(".//thead//tr") ?? table.SelectSingleNode(".//tr[1]");
            if (headerRow == null) return null;

            var headerCells = headerRow.SelectNodes(".//th") ?? headerRow.SelectNodes(".//td");
            if (headerCells == null) return null;

            List<string> columns = new List<string>();
            foreach (var cell in headerCells) columns.Add(SanitizeColumnName(cell.InnerText));

            var dataRows = table.SelectNodes(".//tbody//tr") ?? table.SelectNodes(".//tr[position()>1]");
            if (dataRows == null || dataRows.Count == 0) return null;

            List<ParsedRow> result = new List<ParsedRow>();
            string folderName = Directory.GetParent(filePath).Name;

            foreach (var row in dataRows)
            {
                var cells = row.SelectNodes(".//td");
                if (cells == null || cells.Count < columns.Count || columns.Count == 0) continue;

                ParsedRow pRow = new ParsedRow
                {
                    MaSoThueThuMuc = folderName.Replace("-QL", "").Trim(),
                    TenFile = Path.GetFileName(filePath)
                };

                for (int i = 0; i < columns.Count; i++)
                {
                    string val = Regex.Replace(cells[i].InnerText.Trim(), @"\s+", " ");
                    pRow.Data[columns[i]] = val;
                }
                result.Add(pRow);
            }
            return result;
        }

        // --- HÀM BÓC TÁCH XML (CHỈ ĐỌC RAW, KHÔNG ĐỤNG DB) ---
        private List<ParsedRow> ParseXmlFile(string filePath)
        {
            XDocument xmlDoc = XDocument.Load(filePath);
            var dataRows = new List<XElement>(xmlDoc.Descendants("ROW_CTIET"));
            if (dataRows.Count == 0) return null;

            List<ParsedRow> result = new List<ParsedRow>();
            string folderName = Directory.GetParent(filePath).Name;

            foreach (var rowNode in dataRows)
            {
                ParsedRow pRow = new ParsedRow
                {
                    MaSoThueThuMuc = folderName.Replace("-QL", "").Trim(),
                    TenFile = Path.GetFileName(filePath)
                };

                foreach (var element in rowNode.Elements())
                {
                    string colName = SanitizeColumnName(element.Name.LocalName);
                    string cellValue = "";

                    if (element.HasElements && element.Name.LocalName == "TTIN_GIAYTO")
                    {
                        List<string> giayToInfos = new List<string>();
                        foreach (var g in element.Elements("GIAYTO"))
                        {
                            string tenGiayTo = g.Element("TEN_LOAI_GIAYTO") != null ? g.Element("TEN_LOAI_GIAYTO").Value : "";
                            string soGiayTo = g.Element("SO_GIAYTO") != null ? g.Element("SO_GIAYTO").Value : "";
                            giayToInfos.Add($"{tenGiayTo}: {soGiayTo}");
                        }
                        cellValue = string.Join(" | ", giayToInfos.ToArray());
                    }
                    else
                    {
                        cellValue = element.Value.Trim();
                    }

                    pRow.Data[colName] = cellValue;
                }
                result.Add(pRow);
            }
            return result;
        }

        // --- CÁC HÀM HỖ TRỢ ---
        private string SanitizeColumnName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Cot_Khong_Ten";
            input = Regex.Replace(input, @"\(.*?\)", "").Trim();
            Regex regex = new Regex("\\p{IsCombiningDiacriticalMarks}+");
            string formD = input.Normalize(NormalizationForm.FormD);
            string noDiacritics = regex.Replace(formD, String.Empty).Replace('\u0111', 'd').Replace('\u0110', 'D');
            string safeName = Regex.Replace(noDiacritics, @"[^a-zA-Z0-9]", "_");
            safeName = Regex.Replace(safeName, @"_+", "_").Trim('_');
            if (Regex.IsMatch(safeName, @"^\d")) safeName = "Col_" + safeName;
            return safeName;
        }

        private List<string> GetExistingColumns(SqliteConnection conn, string tableName)
        {
            List<string> columns = new List<string>();
            using (var cmd = new SqliteCommand($"PRAGMA table_info({tableName});", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read()) columns.Add(reader["name"].ToString());
            }
            return columns;
        }

        private void Log(string message)
        {
            if (txtTrangThai.InvokeRequired)
            {
                txtTrangThai.Invoke(new Action(() => Log(message)));
                return;
            }
            txtTrangThai.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            txtTrangThai.ScrollToCaret();
        }
    }
}