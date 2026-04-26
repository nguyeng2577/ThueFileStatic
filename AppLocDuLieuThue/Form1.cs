using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AppLocDuLieuThue
{
    public partial class Form1 : Form
    {
        // Chuỗi kết nối SQLite (Tạo file DB nằm ngay trong thư mục chứa file chạy .exe của tool)
        string dbConnectionString = "Data Source=DuLieuThue.sqlite;Version=3;";

        public Form1()
        {
            InitializeComponent();

            this.btnChonThuMuc.Click += new System.EventHandler(this.btnChonThuMuc_Click);
            this.btnBatDau.Click += new System.EventHandler(this.btnBatDau_Click);
        }

        // --- SỰ KIỆN NÚT CHỌN THƯ MỤC ---
        private void btnChonThuMuc_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Hãy chọn thư mục gốc chứa các file HTML";

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtDuongDan.Text = fbd.SelectedPath;
                    Log("Đã chọn thư mục: " + fbd.SelectedPath);
                }
            }
        }

        // --- SỰ KIỆN NÚT BẮT ĐẦU ---
        private void btnBatDau_Click(object sender, EventArgs e)
        {
            string thuMucDuLieu = txtDuongDan.Text;

            if (string.IsNullOrWhiteSpace(thuMucDuLieu) || !Directory.Exists(thuMucDuLieu))
            {
                MessageBox.Show("Vui lòng chọn một thư mục hợp lệ trước khi bắt đầu!", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Log("Đang khởi tạo Database và bắt đầu quét file...");
            btnBatDau.Enabled = false; // Khóa nút để tránh bấm 2 lần

            try
            {
                // Gọi hàm xử lý cốt lõi
                ProcessHtmlFiles(thuMucDuLieu);
            }
            catch (Exception ex)
            {
                Log("LỖI NGHIÊM TRỌNG: " + ex.Message);
                MessageBox.Show("Có lỗi xảy ra: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnBatDau.Enabled = true; // Mở lại nút
            }
        }

        // --- HÀM XỬ LÝ CỐT LÕI ---
        private void ProcessHtmlFiles(string rootFolderPath)
        {
            int fileThanhCong = 0;
            int fileBoQua = 0;

            using (SQLiteConnection conn = new SQLiteConnection(dbConnectionString))
            {
                conn.Open();

                // 1. Tạo bảng gốc nếu chưa tồn tại
                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS ThongTinToKhai (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        MaSoThue TEXT,
                        TenFile TEXT
                    )";
                using (var cmd = new SQLiteCommand(createTableQuery, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                // Lấy danh sách cột hiện tại
                List<string> existingDbColumns = GetExistingColumns(conn, "ThongTinToKhai");

                // 2. Tìm tất cả file .html
                string[] htmlFiles = Directory.GetFiles(rootFolderPath, "*.html", SearchOption.AllDirectories);
                Log($"Tìm thấy tổng cộng {htmlFiles.Length} file .html. Đang xử lý...");

                foreach (string filePath in htmlFiles)
                {
                    string htmlContent = File.ReadAllText(filePath, Encoding.UTF8);

                    // Lọc file không có dữ liệu
                    if (htmlContent.Contains("Không có dữ liệu thỏa mãn"))
                    {
                        fileBoQua++;
                        continue;
                    }

                    // SỬ DỤNG RÕ RÀNG HtmlAgilityPack.HtmlDocument để tránh lỗi CS0104
                    var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                    htmlDoc.LoadHtml(htmlContent);

                    // Tìm bảng chứa dữ liệu
                    var table = htmlDoc.DocumentNode.SelectSingleNode("//table[contains(@class, 'table-bordered')]");
                    if (table == null)
                    {
                        fileBoQua++;
                        continue;
                    }

                    // 3. Xử lý Tiêu đề (Header)
                    var headerCells = table.SelectNodes(".//thead//th");
                    if (headerCells == null) continue;

                    List<string> fileColumns = new List<string>();
                    foreach (var cell in headerCells)
                    {
                        string colName = SanitizeColumnName(cell.InnerText);
                        fileColumns.Add(colName);

                        // TỰ ĐỘNG THÊM CỘT NẾU CHƯA CÓ TRONG DATABASE
                        if (!existingDbColumns.Contains(colName, StringComparer.OrdinalIgnoreCase))
                        {
                            string addColQuery = $"ALTER TABLE ThongTinToKhai ADD COLUMN {colName} TEXT;";
                            using (var cmd = new SQLiteCommand(addColQuery, conn))
                            {
                                cmd.ExecuteNonQuery();
                            }
                            existingDbColumns.Add(colName);
                            Log($"  -> Đã tạo thêm cột mới trong Database: {colName}");
                        }
                    }

                    // 4. Xử lý Dữ liệu từng dòng
                    var dataRows = table.SelectNodes(".//tbody//tr");
                    if (dataRows != null)
                    {
                        using (var transaction = conn.BeginTransaction())
                        {
                            // Lấy Mã số thuế (vd: "0302956560-QL" -> "0302956560")
                            string folderName = Directory.GetParent(filePath).Name;
                            string maSoThue = folderName.Replace("-QL", "").Trim();
                            string tenFile = Path.GetFileName(filePath);

                            foreach (var row in dataRows)
                            {
                                var cells = row.SelectNodes(".//td");
                                if (cells == null || cells.Count != fileColumns.Count) continue;

                                string colNamesJoined = string.Join(", ", fileColumns);
                                string paramNamesJoined = string.Join(", ", fileColumns.Select(c => "@" + c));

                                string insertQuery = $@"
                                    INSERT INTO ThongTinToKhai (MaSoThue, TenFile, {colNamesJoined}) 
                                    VALUES (@MaSoThue, @TenFile, {paramNamesJoined})";

                                using (var cmd = new SQLiteCommand(insertQuery, conn))
                                {
                                    cmd.Parameters.AddWithValue("@MaSoThue", maSoThue);
                                    cmd.Parameters.AddWithValue("@TenFile", tenFile);

                                    for (int i = 0; i < fileColumns.Count; i++)
                                    {
                                        string cellValue = cells[i].InnerText.Trim();
                                        cellValue = Regex.Replace(cellValue, @"\s+", " "); // Xóa dòng trống thừa

                                        cmd.Parameters.AddWithValue("@" + fileColumns[i], cellValue);
                                    }
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            transaction.Commit();
                        }
                    }
                    fileThanhCong++;
                }

                Log($"HOÀN TẤT! Đã xử lý {fileThanhCong} file. Bỏ qua {fileBoQua} file lỗi/trống.");
                MessageBox.Show($"Xử lý xong!\n- File thành công: {fileThanhCong}\n- File bỏ qua: {fileBoQua}", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // --- HÀM CHUẨN HÓA TÊN CỘT ĐỂ LƯU SQLITE ---
        private string SanitizeColumnName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Cot_Khong_Ten";

            input = Regex.Replace(input, @"\(.*?\)", "").Trim(); // Bỏ chữ trong ngoặc đơn

            Regex regex = new Regex("\\p{IsCombiningDiacriticalMarks}+");
            string formD = input.Normalize(NormalizationForm.FormD);
            string noDiacritics = regex.Replace(formD, String.Empty).Replace('\u0111', 'd').Replace('\u0110', 'D');

            string safeName = Regex.Replace(noDiacritics, @"[^a-zA-Z0-9]", "_");
            safeName = Regex.Replace(safeName, @"_+", "_").Trim('_');

            if (Regex.IsMatch(safeName, @"^\d")) safeName = "Col_" + safeName;

            return safeName;
        }

        // --- LẤY DANH SÁCH CỘT TỪ DATABASE ---
        private List<string> GetExistingColumns(SQLiteConnection conn, string tableName)
        {
            List<string> columns = new List<string>();
            using (var cmd = new SQLiteCommand($"PRAGMA table_info({tableName});", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    columns.Add(reader["name"].ToString());
                }
            }
            return columns;
        }

        // --- HÀM GHI LOG LÊN GIAO DIỆN ---
        private void Log(string message)
        {
            // Đảm bảo cập nhật UI an toàn (nếu sau này bạn chạy Thread/Task)
            if (txtTrangThai.InvokeRequired)
            {
                txtTrangThai.Invoke(new Action(() => Log(message)));
                return;
            }

            txtTrangThai.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            txtTrangThai.ScrollToCaret(); // Tự cuộn xuống dưới cùng
        }
    }
}