namespace HealthixSFTP
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnGenerateTextFile = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // btnGenerateTextFile
            // 
            this.btnGenerateTextFile.Location = new System.Drawing.Point(12, 12);
            this.btnGenerateTextFile.Name = "btnGenerateTextFile";
            this.btnGenerateTextFile.Size = new System.Drawing.Size(190, 52);
            this.btnGenerateTextFile.TabIndex = 0;
            this.btnGenerateTextFile.Text = "Generate Text File";
            this.btnGenerateTextFile.UseVisualStyleBackColor = true;
            this.btnGenerateTextFile.Click += new System.EventHandler(this.btnGenerateTextFile_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(214, 77);
            this.Controls.Add(this.btnGenerateTextFile);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button btnGenerateTextFile;
    }
}

