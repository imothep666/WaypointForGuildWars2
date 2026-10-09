"""Renders each area's painted wash to a transparent PNG through headless Chrome.

The rough, dry-brush edges come from SVG turbulence filters, which Avalonia cannot run, so the
washes are painted once here and shipped as images.
"""
import pathlib, subprocess, sys

out = pathlib.Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
work = pathlib.Path(sys.argv[2]); work.mkdir(parents=True, exist_ok=True)
chrome = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

W, H = 618, 390  # 412 x 260 dp at 1.5x: the washes are soft, and scale up without showing it

FILTERS = '''
<filter id="rough" x="-20%" y="-20%" width="140%" height="140%">
  <feTurbulence type="fractalNoise" baseFrequency="0.013 0.03" numOctaves="4" seed="7" result="n" />
  <feDisplacementMap in="SourceGraphic" in2="n" scale="46" xChannelSelector="R" yChannelSelector="G" result="d" />
  <feGaussianBlur in="d" stdDeviation="1.2" />
</filter>
<filter id="grain">
  <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="1" seed="2" />
  <feColorMatrix values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 .07 0" />
</filter>
<linearGradient id="fade" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#fff" /><stop offset=".72" stop-color="#fff" /><stop offset="1" stop-color="#000" />
</linearGradient>
<mask id="m"><rect width="412" height="260" fill="url(#fade)" /></mask>
'''

WASHES = {
    # White, to be tinted at run time: the opacity mask of a block of a profession's colour.
    "mask": '''
<radialGradient id="g" cx="70%" cy="40%" r="70%">
  <stop offset="0" stop-color="#fff" stop-opacity=".55" />
  <stop offset=".5" stop-color="#fff" stop-opacity=".28" />
  <stop offset="1" stop-color="#fff" stop-opacity="0" />
</radialGradient>|
<path filter="url(#rough)" fill="url(#g)" d="M140 0 C260 -10 430 10 430 80 C430 150 330 200 240 206 C160 212 90 190 70 140 C50 90 80 20 140 0 Z" />
<path filter="url(#rough)" fill="#fff" fill-opacity=".12" d="M250 40 C310 20 400 30 410 70 C418 104 370 124 320 118 C280 112 230 70 250 40 Z" />''',
    "vault": '''
<radialGradient id="g" cx="72%" cy="38%" r="70%">
  <stop offset="0" stop-color="#46B7AE" stop-opacity=".55" />
  <stop offset=".45" stop-color="#2C6F86" stop-opacity=".32" />
  <stop offset="1" stop-color="#5A3E8E" stop-opacity="0" />
</radialGradient>|
<path filter="url(#rough)" fill="url(#g)" d="M120 10 C230 -20 420 0 440 60 C460 140 380 200 300 222 C230 240 150 236 90 200 C40 170 20 110 40 70 C60 30 90 18 120 10 Z" />
<path filter="url(#rough)" fill="#46B7AE" fill-opacity=".16" d="M250 120 C300 96 380 100 400 140 C410 170 360 196 310 190 C270 186 236 150 250 120 Z" />
<g fill="#7FE0D6" fill-opacity=".55">
  <circle cx="352" cy="58" r="2.2" /><circle cx="371" cy="80" r="1.3" /><circle cx="318" cy="38" r="1.1" />
  <circle cx="390" cy="112" r="1.8" /><circle cx="296" cy="72" r=".9" /><circle cx="340" cy="150" r="1.4" />
</g>''',
    "today": '''
<linearGradient id="g" x1="0" y1="1" x2="1" y2="0">
  <stop offset="0" stop-color="#6A2E3A" stop-opacity="0" />
  <stop offset=".45" stop-color="#C2643C" stop-opacity=".38" />
  <stop offset=".8" stop-color="#F2A65A" stop-opacity=".6" />
  <stop offset="1" stop-color="#FFD08A" stop-opacity=".5" />
</linearGradient>|
<path filter="url(#rough)" fill="url(#g)" d="M-30 200 C60 150 170 120 260 70 C320 36 380 10 450 -10 L450 70 C390 100 330 150 260 190 C190 228 90 250 -30 250 Z" />
<path filter="url(#rough)" fill="#FFD08A" fill-opacity=".18" d="M250 60 C300 34 370 14 430 4 L430 40 C380 54 320 80 270 100 C250 106 236 74 250 60 Z" />''',
    "account": '''
<radialGradient id="g" cx="25%" cy="70%" r="75%">
  <stop offset="0" stop-color="#D9A441" stop-opacity=".42" />
  <stop offset=".45" stop-color="#8A5A2A" stop-opacity=".22" />
  <stop offset="1" stop-color="#2A1E14" stop-opacity="0" />
</radialGradient>|
<path filter="url(#rough)" fill="url(#g)" d="M-20 60 C40 20 180 10 280 40 C380 70 420 140 380 190 C340 240 200 250 110 236 C30 224 -30 190 -30 140 C-30 110 -40 80 -20 60 Z" />
<g fill="#F2D68C" fill-opacity=".45">
  <circle cx="60" cy="210" r="1.8" /><circle cx="92" cy="226" r="1.1" /><circle cx="36" cy="180" r="1.3" /><circle cx="140" cy="232" r=".9" />
</g>''',
    "characters": '''
<radialGradient id="g" cx="70%" cy="40%" r="70%">
  <stop offset="0" stop-color="#C08F95" stop-opacity=".5" />
  <stop offset=".5" stop-color="#6E3C52" stop-opacity=".25" />
  <stop offset="1" stop-color="#2A1E2A" stop-opacity="0" />
</radialGradient>|
<path filter="url(#rough)" fill="url(#g)" d="M140 0 C260 -10 430 10 430 80 C430 150 330 200 240 206 C160 212 90 190 70 140 C50 90 80 20 140 0 Z" />''',
    "compass": '''
<radialGradient id="g" cx="30%" cy="35%" r="75%">
  <stop offset="0" stop-color="#5E9FD0" stop-opacity=".5" />
  <stop offset=".5" stop-color="#2E5A86" stop-opacity=".25" />
  <stop offset="1" stop-color="#18263A" stop-opacity="0" />
</radialGradient>|
<path filter="url(#rough)" fill="url(#g)" d="M-30 20 C60 -20 230 -10 330 30 C420 66 430 150 360 190 C280 236 140 230 60 200 C-10 174 -40 110 -30 20 Z" />
<path filter="url(#rough)" fill="#8CC4F0" fill-opacity=".14" d="M40 60 C90 40 170 44 200 80 C220 104 180 130 130 126 C80 122 20 90 40 60 Z" />''',
    "tradingpost": '''
<radialGradient id="g" cx="75%" cy="45%" r="75%">
  <stop offset="0" stop-color="#C8A34E" stop-opacity=".45" />
  <stop offset=".45" stop-color="#8A3A2C" stop-opacity=".28" />
  <stop offset="1" stop-color="#2A1414" stop-opacity="0" />
</radialGradient>|
<path filter="url(#rough)" fill="url(#g)" d="M110 20 C220 -14 420 0 440 70 C456 140 380 206 290 226 C200 244 110 226 70 180 C30 136 40 50 110 20 Z" />''',
}

for name, body in WASHES.items():
    defs, shapes = body.split("|")
    html = f'''<!DOCTYPE html><html><head><style>html,body{{margin:0;background:transparent}}svg{{display:block}}</style></head>
<body><svg width="{W}" height="{H}" viewBox="0 0 412 260" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
<defs>{FILTERS}{defs}</defs>
<g mask="url(#m)">{shapes}</g>
</svg></body></html>'''
    page = work / f"{name}.html"
    page.write_text(html, encoding="utf-8")
    png = out / f"wash_{name}.png"
    subprocess.run([chrome, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000",
                    f"--window-size={W},{H}", f"--screenshot={png}", page.as_uri()],
                   check=True, capture_output=True)
    print(name, png.stat().st_size)
