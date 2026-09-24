from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path
root = Path(__file__).resolve().parent.parent
size = 1024
mask = Image.new('L', (size, size), 0)
curves = [((50,3),(54,3),(54,24),(65,35)),((65,35),(76,46),(97,46),(97,50)),((97,50),(97,54),(76,54),(65,65)),((65,65),(54,76),(54,97),(50,97)),((50,97),(46,97),(46,76),(35,65)),((35,65),(24,54),(3,54),(3,50)),((3,50),(3,46),(24,46),(35,35)),((35,35),(46,24),(46,3),(50,3))]
points=[]
for curve in curves:
 for i in range(61):
  t=i/60;u=1-t
  points.append(tuple((u**3*curve[0][j]+3*u*u*t*curve[1][j]+3*u*t*t*curve[2][j]+t**3*curve[3][j])*size/100 for j in (0,1)))
ImageDraw.Draw(mask).polygon(points,fill=255)
im=Image.new('RGBA',(size,size));d=ImageDraw.Draw(im)
for y in range(size):
 t=y/(size-1);d.line((0,y,size,y),fill=(int(230-79*t),int(224-98*t),int(255-17*t),255))
im.putalpha(mask)
im=im.resize((256,256),Image.Resampling.LANCZOS)
im.save(root/'src'/'app.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.save(root/'src'/'logo.png')
wizard=Image.new('RGB',(330,630),'#171820')
glow=Image.new('RGBA',wizard.size)
ImageDraw.Draw(glow).ellipse((50,220,280,410),fill=(117,83,202,64))
wizard=Image.alpha_composite(wizard.convert('RGBA'),glow.filter(ImageFilter.GaussianBlur(55)))
star=im.resize((158,158),Image.Resampling.LANCZOS)
wizard.alpha_composite(star,(86,236))
wizard.convert('RGB').save(root/'src'/'wizard.bmp')
