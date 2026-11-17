using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GymClock.Wpf
{
    /// <summary>Base for every view model: raises PropertyChanged so bindings update.</summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Sets a field and tells bindings, if the value changed. Returns true if it did.</summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise([CallerMemberName] string name = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
