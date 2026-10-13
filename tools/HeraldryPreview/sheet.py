import sys, os, numpy as np
from PIL import Image, ImageDraw, ImageFont
# sheet.py <listfile> <dir> <out.png> <cols> [tile_w]
lst, d, out, cols = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
tw = int(sys.argv[5]) if len(sys.argv) > 5 else 150
lines = [l.strip() for l in open(lst) if l.strip() and not l.startswith('//')]
bump = np.asarray(Image.open('../../ValheimHeraldryMod/Art/Banner Bump Map.png').convert('RGB')).astype(float)/255
first = Image.open(os.path.join(d, '00.png')); W, H = first.size
th = int(tw * H / W)
nb = np.asarray(Image.fromarray((bump*255).astype('uint8')).resize((W, H), Image.BILINEAR)).astype(float)/255
n = nb*2-1; L = np.array([-0.35, 0.45, 0.82]); L /= np.linalg.norm(L)
shade = np.clip(0.55 + 0.5*(n@L), 0.6, 1.15)
tall = W/H < 0.5
try: font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 11)
except: font = ImageFont.load_default()
rows = (len(lines)+cols-1)//cols
pad, lab = 12, 30
sheet = Image.new('RGB', (cols*(tw+pad)+pad, rows*(th+lab+pad)+pad), (58, 54, 50))
dr = ImageDraw.Draw(sheet)
for i, text in enumerate(lines):
    im = np.asarray(Image.open(os.path.join(d, f'{i:02d}.png')).convert('RGB')).astype(float)
    im = np.clip(im*shade[..., None], 0, 255).astype('uint8')
    tile = Image.fromarray(im).resize((tw, th), Image.LANCZOS).convert('RGBA')
    if tall:  # approximate pointed bottom of the vanilla hanging banner
        m = Image.new('L', (tw, th), 255); md = ImageDraw.Draw(m)
        y0 = int(th*0.84); md.polygon([(0, y0), (tw/2, th), (0, th)], fill=0); md.polygon([(tw, y0), (tw/2, th), (tw, th)], fill=0)
        tile.putalpha(m)
    x = pad + (i % cols)*(tw+pad); y = pad + (i//cols)*(th+lab+pad)
    sheet.paste(tile, (x, y), tile)
    # wrap label
    words, ln, out_lines = text.split(), '', []
    for wd in words:
        if dr.textlength(ln+' '+wd, font=font) > tw: out_lines.append(ln); ln = wd
        else: ln = (ln+' '+wd).strip()
    out_lines.append(ln)
    for k, l in enumerate(out_lines[:2]): dr.text((x, y+th+3+k*13), l, fill=(230, 225, 210), font=font)
sheet.save(out)
print(out, sheet.size)
