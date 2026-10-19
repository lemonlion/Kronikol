# S9a's doc comment on ReportGenerator.SetupMarker named StepBarPlantUml from Kronikol.Reports, where it does not
# resolve (CS1574). usage: python fix-s9a-cref.py <root>
import sys
p = sys.argv[1].rstrip('/') + '/src/Kronikol/Reports/ReportGenerator.cs'
s = open(p, encoding='utf-8-sig', newline='').read()
a = 'a When step bar in either of the forms <see cref="StepBarPlantUml"/> writes.'
b = 'a When step bar in either of the forms\n    /// <see cref="PlantUml.StepBarPlantUml"/> writes.'
assert s.count(a) == 1, 'anchor'
open(p, 'w', encoding='utf-8-sig', newline='').write(s.replace(a, b))
print('ok')
