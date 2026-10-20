# The violet theme's dark values (plans/OPTION_C_PLAN.md S13): each is the token sheet's own dark value turned, in OKLCH,
# from its hue to the theme's (the hue of #8b5cf6, 292.7) with its lightness and chroma kept (the chroma cut only as far as
# sRGB needs), so it keeps the lightness the token sheet's contrast audit was done at. Prints each value and the WCAG
# contrasts the theme relies on, light and dark, with the ones it fixes beside what 4.x's rules gave.
# usage: python violet-palette.py
import math


def srgb_to_lin(c):
    c /= 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin_to_srgb(c):
    return (12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055) * 255


def hex_rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def rgb_hex(rgb):
    return '#' + ''.join(f'{max(0, min(255, round(c))):02x}' for c in rgb)


def to_oklab(rgb):
    r, g, b = (srgb_to_lin(c) for c in rgb)
    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l, m, s = (math.copysign(abs(x) ** (1 / 3), x) for x in (l, m, s))
    return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s)


def from_oklab(L, a, b):
    l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s)


def lch(hexv):
    L, a, b = to_oklab(hex_rgb(hexv))
    return L, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360


def turn(hexv, hue):
    L, C, _ = lch(hexv)
    for k in range(1000):
        c = C * (1 - k / 1000)
        lin = from_oklab(L, c * math.cos(math.radians(hue)), c * math.sin(math.radians(hue)))
        if all(-1e-6 <= x <= 1 + 1e-6 for x in lin):
            return rgb_hex([lin_to_srgb(max(0, min(1, x))) for x in lin])
    raise ValueError(hexv)


def contrast(x, y):
    def lum(h):
        r, g, b = (srgb_to_lin(c) for c in hex_rgb(h))
        return 0.2126 * r + 0.7152 * g + 0.0722 * b
    a, b = sorted((lum(x), lum(y)), reverse=True)
    return (a + 0.05) / (b + 0.05)


HUE = lch('#8b5cf6')[2]
print(f'the theme hue (OKLCH) of #8b5cf6: {HUE:.1f}')

# the token sheet's dark values (report-tokens.css 117-214) for the names the theme sets; --violet-card is the dark
# --kron-card turned, --violet-rail the dark --kron-hairline turned
DARK = {
    '--kron-accent': '#7ea6f2', '--kron-hover': '#222b38', '--kron-selected': '#1f3254', '--kron-selected-ink': '#b0c9fb',
    '--kron-selected-edge': '#5b82cf', '--kron-field': '#1f2732', '--kron-strip': '#1b222c', '--kron-pass': '#62cd8f',
    '--kron-pass-soft': '#15301f', '--kron-bypass': '#8ab1f6', '--kron-bypass-soft': '#1c2a45', '--violet-card': '#171d26',
    '--violet-rail': '#343e4d',
}
t = {k: turn(v, HUE) for k, v in DARK.items()}
for k, v in DARK.items():
    print(f'{k:22} {v} -> {t[k]}')


def show(label, fg, bg):
    print(f'{label:62} {fg} on {bg}: {contrast(fg, bg):.2f}')


INK_L, INK_D, GROUND_D, CARD_D = '#242b36', '#e4e9f0', '#0e131a', '#171d26'
print('--- light')
show('a chosen control: white on --violet-solid (4.x, under AA)', '#ffffff', '#8b5cf6')
show('a chosen tab hovered: white on --violet-solid-hover', '#ffffff', '#7c3aed')
show('--kron-accent text on the violet card', '#6d28d9', '#ddd6fe')
show('--kron-accent text on white', '#6d28d9', '#ffffff')
show('  4.x: accent text #8b5cf6 on the violet card', '#8b5cf6', '#ddd6fe')
show('ink on the violet card', INK_L, '#ddd6fe')
show('a label: --kron-selected-ink on --kron-selected', '#5b21b6', '#c4b5fd')
show('  4.x: --kron-muted on #c4b5fd', '#5b6474', '#c4b5fd')
show('an attachment name: --kron-selected-ink on the violet card', '#5b21b6', '#ddd6fe')
show('  4.x: #a78bfa on the violet card', '#a78bfa', '#ddd6fe')
show('the search field edge: --kron-selected-edge on the violet card', '#8b5cf6', '#ddd6fe')
show('  4.x: #c4b5fd on the violet card', '#c4b5fd', '#ddd6fe')
show('the passed tick: white on --violet-solid', '#ffffff', '#8b5cf6')
show('  4.x: the green tick on solid violet', '#276c44', '#8b5cf6')
show('back to top: white on --violet-solid', '#ffffff', '#8b5cf6')
show('  4.x: --kron-ink-soft on solid violet', '#4a5361', '#8b5cf6')
show('  4.x: the search help, ink on #1e1534', INK_L, '#1e1534')
show('the sub-steps rail: --violet-rail on the violet card', '#a78bfa', '#ddd6fe')
show('  4.x: #ddd6fe on the violet card (scenarios sit on it since 5.0.0)', '#ddd6fe', '#ddd6fe')
print('--- dark')
show('ink on the violet card', INK_D, t['--violet-card'])
show('--kron-accent on the dark card', t['--kron-accent'], CARD_D)
show('a label: --kron-selected-ink on --kron-selected', t['--kron-selected-ink'], t['--kron-selected'])
show('a chosen control: white on --violet-solid (#7c3aed)', '#ffffff', '#7c3aed')
show('a chosen tab hovered: white on --violet-solid-hover (#6d28d9)', '#ffffff', '#6d28d9')
show('a hover edge: --violet-edge on --kron-hover', '#a78bfa', t['--kron-hover'])
show('--kron-pass on --kron-pass-soft', t['--kron-pass'], t['--kron-pass-soft'])
show('--kron-bypass on --kron-bypass-soft', t['--kron-bypass'], t['--kron-bypass-soft'])
show('the search field edge: --kron-selected-edge on the violet card', t['--kron-selected-edge'], t['--violet-card'])
show('the sub-steps rail: --violet-rail on the violet card', t['--violet-rail'], t['--violet-card'])
show('  the default theme: --kron-hairline on the dark card', '#343e4d', CARD_D)
show('the violet card on the ground', t['--violet-card'], GROUND_D)
