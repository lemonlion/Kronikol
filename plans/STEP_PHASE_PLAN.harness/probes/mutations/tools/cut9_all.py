"""Every measured cut 9 mutant again, read with the known races."""
import compare
from mutations import ORDER
for mutant in ORDER:
    v = compare.compare_mutant(mutant)
    print(f"{mutant} {v.word} solid={v.solid} order={v.order} racing-new={v.racing_new} racing-seen={v.racing_seen} first: {v.first[:150]}")
