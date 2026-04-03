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
            pictureBox1 = new PictureBox();
            panelBottom = new Panel();
            btnExit = new Button();
            lblSpecs = new Label();
            panelCenter = new Panel();
            panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            panelBottom.SuspendLayout();
            SuspendLayout();
            // 
            // panelTop
            // 
            panelTop.BackColor = Color.Black;
            panelTop.Controls.Add(lblNextSong);
            panelTop.Controls.Add(lblNowPlaying);
            panelTop.Controls.Add(pictureBox1);
            panelTop.Dock = DockStyle.Top;
            panelTop.Location = new Point(0, 0);
            panelTop.Name = "panelTop";
            panelTop.Size = new Size(1264, 100);
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
            // pictureBox1
            // 
            pictureBox1.Location = new Point(1125, 3);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(136, 94);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // panelBottom
            // 
            panelBottom.BackColor = Color.Black;
            panelBottom.Controls.Add(btnExit);
            panelBottom.Controls.Add(lblSpecs);
            panelBottom.Dock = DockStyle.Bottom;
            panelBottom.Location = new Point(0, 637);
            panelBottom.Name = "panelBottom";
            panelBottom.Size = new Size(1264, 44);
            panelBottom.TabIndex = 1;
            // 
            // btnExit
            // 
            btnExit.Location = new Point(1177, 12);
            btnExit.Name = "btnExit";
            btnExit.Size = new Size(75, 23);
            btnExit.TabIndex = 0;
            btnExit.Text = "btnExit";
            btnExit.UseVisualStyleBackColor = true;
            btnExit.Click += btnExit_Click;
            // 
            // lblSpecs
            // 
            lblSpecs.AutoSize = true;
            lblSpecs.Font = new Font("Segoe UI", 12F);
            lblSpecs.ForeColor = Color.Gray;
            lblSpecs.Location = new Point(12, 14);
            lblSpecs.Name = "lblSpecs";
            lblSpecs.Size = new Size(67, 21);
            lblSpecs.TabIndex = 0;
            lblSpecs.Text = "lblSpecs";
            // 
            // panelCenter
            // 
            panelCenter.BackColor = Color.Black;
            panelCenter.Dock = DockStyle.Fill;
            panelCenter.Location = new Point(0, 100);
            panelCenter.Name = "panelCenter";
            panelCenter.Size = new Size(1264, 537);
            panelCenter.TabIndex = 2;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 681);
            Controls.Add(panelCenter);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Karaoke_Home";
            Load += MainForm_Load;
            panelTop.ResumeLayout(false);
            panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            panelBottom.ResumeLayout(false);
            panelBottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel panelTop;
        private Panel panelBottom;
        private Panel panelCenter;
        private PictureBox pictureBox1;
        private Label lblNowPlaying;
        private Label lblNextSong;
        private Label lblSpecs;
        private Button btnExit;
    }
}
