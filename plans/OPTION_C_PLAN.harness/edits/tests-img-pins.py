# D43 follow-up: four StepRenderingReportTests pins read "<img" anywhere in the page. D43's context menu script carries
# '<img alt="Diagram"' in its text, so the two "no image" facts failed and the two "an image" facts passed whatever the
# attachment drew. They read the attachment's own markup now. usage: python tests-img-pins.py <root>
import sys

p = sys.argv[1].rstrip('/') + '/tests/Kronikol.Tests/Reports/StepRenderingReportTests.cs'
raw = open(p, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
s = raw.decode('utf-8-sig')
yes = '        Assert.Contains("<img", content);\n'
no = '        Assert.DoesNotContain("<img", content);\n'
assert s.count(yes) == 2 and s.count(no) == 2, (s.count(yes), s.count(no))
s = s.replace(yes, '        // the attachment\'s own image (the page\'s scripts carry "<img" in their text)\n'
                   '        Assert.Contains("<img class=\\"attachment-image\\"", content);\n')
s = s.replace(no, '        // no image, image link or image name for the attachment (the page\'s scripts carry "<img" in their text)\n'
                  '        Assert.DoesNotContain("class=\\"attachment-image", content);\n')
open(p, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s)
print('ok: StepRenderingReportTests.cs')
