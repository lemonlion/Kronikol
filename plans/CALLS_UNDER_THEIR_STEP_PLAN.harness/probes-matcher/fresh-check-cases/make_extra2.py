"""K1 with the two rows' durations apart. Usage: make_extra2.py <out dir>"""
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, r"C:/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness/probes-matcher/review-cases")
from lib import *
R = sys.argv[1]
c = Case(R, "N4-K1-durations-apart")
c.start("s","t",0); c.step("s","a basket","Given",100,dur=100); c.step("s","sub one","Given",110,level=1); c.step("s","a basket","Given",200,dur=120); c.step("s","sub two","Given",210,level=1); c.pair("s","x",220); c.end("s",500); c.write()
