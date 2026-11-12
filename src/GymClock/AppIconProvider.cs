using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    internal static class AppIconProvider
    {
        private static Icon _cached;

        public static Icon GetIcon()
        {
            if (_cached != null) return _cached;

            try
            {
                _cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                _cached = null;
            }

            return _cached;
        }
    }
}
