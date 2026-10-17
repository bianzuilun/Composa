namespace Composa.Model;

public enum CanvasUnit { Pixels, Inches, Centimeters, Millimeters }

public static class CanvasUnits
{
    public static double PerInch(CanvasUnit unit) => unit switch { CanvasUnit.Centimeters => 2.54, CanvasUnit.Millimeters => 25.4, _ => 1 };
    public static double FromPixels(double pixels, CanvasUnit unit, double resolution) => unit == CanvasUnit.Pixels ? pixels : pixels / resolution * PerInch(unit);
    public static int ToPixels(double value, CanvasUnit unit, double resolution)
    {
        if (!double.IsFinite(value) || !double.IsFinite(resolution) || value <= 0 || resolution <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return (int)Math.Clamp(Math.Round(unit == CanvasUnit.Pixels ? value : value / PerInch(unit) * resolution), 1, DocumentLimits.MaxSide);
    }
}
