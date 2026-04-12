namespace karaoke_home
{
    partial class MainForm
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
            panelTop = new Panel();
            lblNextSong = new Label();
            lblNowPlaying = new Label();
            picQRCode = new PictureBox();
            panelCenter = new Panel();
            panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picQRCode).BeginInit();
            SuspendLayout();
            // 
            // panelTop
            // 
            panelTop.BackColor = Color.Black;
            panelTop.Controls.Add(lblNextSong);
            panelTop.Controls.Add(lblNowPlaying);
            panelTop.Controls.Add(picQRCode);
            panelTop.Dock = DockStyle.Top;
            panelTop.Location = new Point(0, 0);
            panelTop.Name = "panelTop";
            panelTop.Size = new Size(1584, 100);
            panelTop.TabIndex = 0;
            // 
            // lblNextSong
            // 
            lblNextSong.AutoSize = true;
            lblNextSong.Font = new Font("Segoe UI", 12F);
            lblNextSong.ForeColor = Color.White;
            lblNextSong.Location = new Point(20, 57);
            lblNextSong.Name = "lblNextSong";
            lblNextSong.Size = new Size(95, 21);
            lblNextSong.TabIndex = 3;
            lblNextSong.Text = "lblNextSong";
            // 
            // lblNowPlaying
            // 
            lblNowPlaying.AutoSize = true;
            lblNowPlaying.Font = new Font("Segoe UI", 12F);
            lblNowPlaying.ForeColor = Color.White;
            lblNowPlaying.Location = new Point(20, 14);
            lblNowPlaying.Name = "lblNowPlaying";
            lblNowPlaying.Size = new Size(111, 21);
            lblNowPlaying.TabIndex = 2;
            lblNowPlaying.Text = "lblNowPlaying";
            // 
            // picQRCode
            // 
            picQRCode.Location = new Point(1436, 6);
            picQRCode.Name = "picQRCode";
            picQRCode.Size = new Size(136, 94);
            picQRCode.SizeMode = PictureBoxSizeMode.Zoom;
            picQRCode.TabIndex = 0;
            picQRCode.TabStop = false;
            // 
            // panelCenter
            // 
            panelCenter.BackColor = Color.Black;
            panelCenter.Dock = DockStyle.Fill;
            panelCenter.Location = new Point(0, 100);
            panelCenter.Name = "panelCenter";
            panelCenter.Size = new Size(1584, 761);
            panelCenter.TabIndex = 2;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1584, 861);
            Controls.Add(panelCenter);
            Controls.Add(panelTop);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Karaoke_Home";
            Load += MainForm_Load;
            panelTop.ResumeLayout(false);
            panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picQRCode).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Panel panelTop;
        private Panel panelCenter;
        private PictureBox picQRCode;
        private Label lblNowPlaying;
        private Label lblNextSong;
    }
}
