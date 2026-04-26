namespace AppLocDuLieuThue
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            btnChonThuMuc = new Button();
            btnBatDau = new Button();
            txtDuongDan = new TextBox();
            txtTrangThai = new RichTextBox();
            label2 = new Label();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(46, 47);
            label1.Name = "label1";
            label1.Size = new Size(284, 20);
            label1.TabIndex = 0;
            label1.Text = "1. Chọn thư mục chứa các file HTML (-QL)";
            // 
            // btnChonThuMuc
            // 
            btnChonThuMuc.Location = new Point(356, 84);
            btnChonThuMuc.Name = "btnChonThuMuc";
            btnChonThuMuc.Size = new Size(94, 29);
            btnChonThuMuc.TabIndex = 1;
            btnChonThuMuc.Text = "Chọn..";
            btnChonThuMuc.UseVisualStyleBackColor = true;
            // 
            // btnBatDau
            // 
            btnBatDau.Location = new Point(172, 152);
            btnBatDau.Name = "btnBatDau";
            btnBatDau.Size = new Size(158, 29);
            btnBatDau.TabIndex = 2;
            btnBatDau.Text = "Bắt đầu Xử lý";
            btnBatDau.UseVisualStyleBackColor = true;
            // 
            // txtDuongDan
            // 
            txtDuongDan.Location = new Point(46, 86);
            txtDuongDan.Name = "txtDuongDan";
            txtDuongDan.ReadOnly = true;
            txtDuongDan.Size = new Size(284, 27);
            txtDuongDan.TabIndex = 3;
            // 
            // txtTrangThai
            // 
            txtTrangThai.Location = new Point(46, 242);
            txtTrangThai.Name = "txtTrangThai";
            txtTrangThai.ReadOnly = true;
            txtTrangThai.Size = new Size(404, 179);
            txtTrangThai.TabIndex = 4;
            txtTrangThai.Text = "";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(46, 208);
            label2.Name = "label2";
            label2.Size = new Size(166, 20);
            label2.TabIndex = 5;
            label2.Text = "2. Trạng thái hoạt động:";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(503, 450);
            Controls.Add(label2);
            Controls.Add(txtTrangThai);
            Controls.Add(txtDuongDan);
            Controls.Add(btnBatDau);
            Controls.Add(btnChonThuMuc);
            Controls.Add(label1);
            Name = "Form1";
            Text = "Phần mềm Trích xuất Dữ Liệu Thuế (SQLite)";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button btnChonThuMuc;
        private Button btnBatDau;
        private TextBox txtDuongDan;
        private RichTextBox txtTrangThai;
        private Label label2;
    }
}
