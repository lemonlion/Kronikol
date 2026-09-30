# Wiki patches this plan's session could not push

The session that executed the plan could push `main` but not the wiki (`lemonlion/Kronikol.wiki` was not in its
authorised repositories), nor tags (the git proxy refused `refs/tags/*` with HTTP 403). Each release's wiki commit is
here as a `git format-patch` file, in order. To publish them, in a clone of the wiki beside this repository:

```
git -C ../Kronikol.wiki am ../Kronikol/plans/INGEST_FIDELITY_PLAN.harness/wiki/*.patch
git -C ../Kronikol.wiki push
```

and delete this folder once they are on the wiki. The tags the same session could not push are listed in the plan's
§12 Log; each is the `Release x.y.z` commit of that version on `main`.
