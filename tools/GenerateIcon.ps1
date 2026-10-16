Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()

foreach ($size in $sizes) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $scale = $size / 256.0
    $context.PushTransform([System.Windows.Media.ScaleTransform]::new($scale, $scale))

    $context.DrawRoundedRectangle(
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#0B0C0D")),
        $null,
        [System.Windows.Rect]::new(0, 0, 256, 256),
        38,
        38)

    $borderPen = [System.Windows.Media.Pen]::new(
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#2B302D")),
        8)
    $context.DrawRoundedRectangle(
        $null,
        $borderPen,
        [System.Windows.Rect]::new(18, 18, 220, 220),
        24,
        24)

    $light = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#E7E9E8"))
    $green = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#00E676"))
    $context.DrawGeometry($light, $null, [System.Windows.Media.Geometry]::Parse("M55,165 L55,88 L80,88 L111,127 L142,88 L167,88 L167,165 L144,165 L144,122 L111,162 L78,122 L78,165 Z"))
    $context.DrawGeometry($green, $null, [System.Windows.Media.Geometry]::Parse("M184,88 L184,131 L164,131 L199,169 L234,131 L214,131 L214,88 Z"))

    $context.Pop()
    $context.Close()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    $encoder.Save($stream)
    $images += ,$stream.ToArray()
    $stream.Dispose()
}

$output = [System.IO.File]::Create((Join-Path $PSScriptRoot "..\assets\app.ico"))
$writer = [System.IO.BinaryWriter]::new($output)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)

$offset = 6 + (16 * $images.Count)
for ($i = 0; $i -lt $images.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}

foreach ($image in $images) {
    $writer.Write($image)
}

$writer.Dispose()
$output.Dispose()
