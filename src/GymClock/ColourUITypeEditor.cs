using System;
using System.Drawing;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace GymClock
{
    /// <summary>
    /// PropertyGrid editor for the station/block "Colour" text property - shows the
    /// standard colour picker dialog instead of a plain text box, and writes the
    /// result back as "#RRGGBB" so it still round-trips through the script grammar
    /// and TimerSettings.ParseColour exactly like a hand-typed value.
    /// </summary>
    public class ColourUITypeEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(System.ComponentModel.ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.Modal;
        }

        public override object EditValue(System.ComponentModel.ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            string text = value as string ?? string.Empty;
            Color initial = TimerSettings.ParseColour(text, Color.White);

            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = initial;
                dialog.FullOpen = true;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    Color c = dialog.Color;
                    return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
                }
            }

            return value;
        }
    }
}
