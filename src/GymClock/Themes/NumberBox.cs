using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GymClock.Wpf
{
    /// <summary>
    /// A text box for a whole number of seconds, rounds and so on: digits only, kept between
    /// <see cref="Minimum"/> and <see cref="Maximum"/>, with up and down arrows (and the Up and
    /// Down keys) that step by <see cref="Step"/>. Bind <see cref="Value"/>, not Text.
    /// Its look is the NumberBox style in Controls.xaml.
    /// </summary>
    public class NumberBox : TextBox
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(int), typeof(NumberBox),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(int), typeof(NumberBox), new PropertyMetadata(0, OnRangeChanged));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(int), typeof(NumberBox), new PropertyMetadata(9999, OnRangeChanged));

        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
            nameof(Step), typeof(int), typeof(NumberBox), new PropertyMetadata(1));

        private RepeatButton _up;
        private RepeatButton _down;

        public NumberBox()
        {
            Text = "0";
            DataObject.AddPastingHandler(this, OnPasting);
        }

        public int Value
        {
            get { return (int)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public int Minimum
        {
            get { return (int)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public int Maximum
        {
            get { return (int)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public int Step
        {
            get { return (int)GetValue(StepProperty); }
            set { SetValue(StepProperty, value); }
        }

        public override void OnApplyTemplate()
        {
            if (_up != null) _up.Click -= OnUp;
            if (_down != null) _down.Click -= OnDown;
            base.OnApplyTemplate();
            _up = GetTemplateChild("PART_Up") as RepeatButton;
            _down = GetTemplateChild("PART_Down") as RepeatButton;
            if (_up != null) _up.Click += OnUp;
            if (_down != null) _down.Click += OnDown;
        }

        /// <summary>Reads what was typed into <see cref="Value"/> (kept in range) and shows the result.</summary>
        public void Commit()
        {
            int typed;
            if (int.TryParse(Text, NumberStyles.None, CultureInfo.InvariantCulture, out typed)) Value = typed;
            ShowValue();
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            if (!AllDigits(e.Text)) e.Handled = true;
            base.OnPreviewTextInput(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up: StepBy(+1); e.Handled = true; break;
                case Key.Down: StepBy(-1); e.Handled = true; break;
                case Key.Enter: Commit(); break;
                case Key.Space: e.Handled = true; break;     // a space is not a digit
            }
            base.OnPreviewKeyDown(e);
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            Commit();
            base.OnLostKeyboardFocus(e);
        }

        private void OnUp(object sender, RoutedEventArgs e) { StepBy(+1); }
        private void OnDown(object sender, RoutedEventArgs e) { StepBy(-1); }

        private void StepBy(int direction)
        {
            Commit();
            long next = (long)Value + (long)direction * Math.Max(1, Step);
            Value = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, next));
            ShowValue();
            CaretIndex = Text.Length;
        }

        private void ShowValue()
        {
            string text = Value.ToString(CultureInfo.InvariantCulture);
            if (Text != text) Text = text;
        }

        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            string text = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
                ? e.DataObject.GetData(DataFormats.UnicodeText) as string
                : null;
            if (text == null || !AllDigits(text.Trim())) e.CancelCommand();
        }

        private static bool AllDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        private static object CoerceValue(DependencyObject d, object value)
        {
            NumberBox box = (NumberBox)d;
            int v = (int)value;
            if (v < box.Minimum) v = box.Minimum;
            if (v > box.Maximum) v = box.Maximum;
            return v;
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((NumberBox)d).ShowValue();
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            d.CoerceValue(ValueProperty);
        }
    }
}
