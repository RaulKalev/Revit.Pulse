using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Pulse.UI
{
    /// <summary>
    /// Animates a <see cref="GridLength"/> value (pixels only — not Star/Auto).
    /// Used to smoothly animate ColumnDefinition.Width and RowDefinition.Height.
    /// </summary>
    public class GridLengthAnimation : AnimationTimeline
    {
        // ── Dependency properties ─────────────────────────────────────────────

        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(GridLength?), typeof(GridLengthAnimation));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(GridLength?), typeof(GridLengthAnimation));

        public static readonly DependencyProperty EasingFunctionProperty =
            DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation));

        public GridLength? From
        {
            get => (GridLength?)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public GridLength? To
        {
            get => (GridLength?)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public IEasingFunction EasingFunction
        {
            get => (IEasingFunction)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        // ── AnimationTimeline overrides ───────────────────────────────────────

        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public override object GetCurrentValue(
            object defaultOriginValue,
            object defaultDestinationValue,
            AnimationClock animationClock)
        {
            double progress = animationClock.CurrentProgress ?? 0.0;

            if (EasingFunction != null)
                progress = EasingFunction.Ease(progress);

            double from = From.HasValue ? From.Value.Value
                        : (defaultOriginValue is GridLength gl ? gl.Value : 0);
            double to   = To.HasValue   ? To.Value.Value
                        : (defaultDestinationValue is GridLength gd ? gd.Value : 0);

            return new GridLength(from + (to - from) * progress);
        }
    }
}
