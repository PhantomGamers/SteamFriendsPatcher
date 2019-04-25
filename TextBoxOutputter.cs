// From TechNet
// https://social.technet.microsoft.com/wiki/contents/articles/12347.wpf-howto-add-a-debugoutput-console-to-your-application.aspx
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SteamFriendsPatcher
{
    public class TextBoxOutputter : TextWriter
    {
#pragma warning disable IDE0044 // Add readonly modifier
        private RichTextBox richtextBox = null;
#pragma warning restore IDE0044 // Add readonly modifier

        public TextBoxOutputter(RichTextBox output)
        {
            richtextBox = output;
        }

        public override void Write(char value)
        {
            base.Write(value);
            richtextBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                richtextBox.AppendText(value.ToString());
            }));
        }

        public override Encoding Encoding
        {
            get { return System.Text.Encoding.UTF8; }
        }
    }
}