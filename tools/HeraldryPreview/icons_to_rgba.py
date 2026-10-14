# icons_to_rgba.py <png folder> <out folder>: converts icon PNGs to the raw .rgba files Program.cs loads
import sys, os, struct
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)
for f in os.listdir(src):
    if not f.lower().endswith('.png'): continue
    im = Image.open(os.path.join(src, f)).convert('RGBA')
    with open(os.path.join(dst, f[:-4].lower() + '.rgba'), 'wb') as o:
        o.write(struct.pack('<ii', *im.size)); o.write(im.tobytes())
