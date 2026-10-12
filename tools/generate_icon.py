"""Generate the application-owned geometric icon. Development tool; Pillow is not shipped."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
im = Image.new("RGBA", (1024, 1024))
d = ImageDraw.Draw(im)
d.rounded_rectangle((20, 20, 1004, 1004), radius=215, fill="#152130")
d.rounded_rectangle((200, 245, 757, 877), radius=64, fill="#486879")
d.rounded_rectangle((257, 182, 814, 814), radius=64, fill="#87D8D0")
d.polygon([(335, 115), (641, 115), (834, 308), (834, 728), (335, 728)], fill="#F0F5FC")
d.polygon([(641, 115), (641, 308), (834, 308)], fill="#B3C6D9")
for y, length in [(412, 306), (503, 306), (594, 215)]:
    d.rounded_rectangle((414, y, 414 + length, y + 34), radius=17, fill="#486879")
path = root / "src/PDFToolbox.Windows/Assets/App.ico"
im.save(path, format="ICO", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.resize((256,256), Image.Resampling.LANCZOS).save(path.with_suffix(".png"))
