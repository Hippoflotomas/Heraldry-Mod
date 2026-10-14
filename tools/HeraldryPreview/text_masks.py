"""text_masks.py <folder>: makes the lettering masks the preview tool asks for (missing.txt), with a system font
(the game uses its own Norse font; this is only for looking at layouts)."""
import sys, os, struct
from PIL import Image, ImageDraw, ImageFont
d = sys.argv[1]
miss = os.path.join(d, 'missing.txt')
if not os.path.exists(miss): sys.exit()
font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 96)
for text in set(l.rstrip('\n') for l in open(miss, encoding='utf-8') if l.strip()):
    l, t, r, b = font.getbbox(text)
    im = Image.new('L', (r - l + 16, b - t + 16), 0)
    ImageDraw.Draw(im).text((8 - l, 8 - t), text, fill=255, font=font)
    with open(os.path.join(d, text.encode('utf-8').hex().upper() + '.mask'), 'wb') as f:
        f.write(struct.pack('<ii', im.width, im.height)); f.write(im.tobytes())
os.remove(miss)
