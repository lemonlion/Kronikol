def lin(c):
    c/=255
    return c/12.92 if c<=0.04045 else ((c+0.055)/1.055)**2.4
def lum(h):
    h=h.lstrip('#'); r,g,b=int(h[0:2],16),int(h[2:4],16),int(h[4:6],16)
    return 0.2126*lin(r)+0.7152*lin(g)+0.0722*lin(b)
def cr(fg,bg):
    a,b=lum(fg),lum(bg); a,b=max(a,b),min(a,b)
    return (a+0.05)/(b+0.05)
fills={'default note #FEFFDD':'#FEFFDD','event note #CFECF7':'#CFECF7','pass hnote #D4EDDA':'#D4EDDA','fail hnote #F8D7DA':'#F8D7DA','setup partition #F6F6F6':'#F6F6F6','white page':'#FFFFFF'}
print('today #808080 (gray):')
for k,v in fills.items(): print(f'  {k:28s} {cr("#808080",v):.2f}')
print('lightgray #D3D3D3 (FocusDeEmphasis, & divider):')
for k,v in fills.items(): print(f'  {k:28s} {cr("#D3D3D3",v):.2f}')
print()
print('lightest neutral grey clearing a target on each fill:')
for target in (4.5,3.0):
    print(f' target {target}:')
    for k,v in fills.items():
        for g in range(255,0,-1):
            h='#%02X%02X%02X'%(g,g,g)
            if cr(h,v)>=target:
                print(f'  {k:28s} {h}  {cr(h,v):.2f}'); break
print()
print('candidates on default / event / pass / fail:')
for h in ('#808080','#767676','#757575','#707070','#6E6E6E','#6B6B6B','#686868','#666666','#606060','#595959'):
    print(f'  {h}  ' + '  '.join(f'{cr(h,v):.2f}' for v in ('#FEFFDD','#CFECF7','#D4EDDA','#F8D7DA','#FFFFFF')))
print()
print('black text on fills (body text today):')
for k,v in fills.items(): print(f'  {k:28s} {cr("#000000",v):.2f}')
