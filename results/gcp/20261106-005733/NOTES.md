# GKE run 2026-11-06 (europe-central2-a)

Cluster: GKE Standard, master node + 4 worker nodes, all e2-standard-2 (2 vCPU, 8 GB);
one parcs8 daemon per worker node (1 CPU, 2 GiB limit); sequential baseline as a Job on
worker-1 with the same 1 CPU / 2 GiB limit. Each cell: 1 warmup + 5 measured rounds over
the same started points; median reported.

- validation.txt: module v1, all 9 datasets x N=1..4 identical to the RPP reference.
- validation-module-v2.txt: same check after the merge change below.
- natural_* PARCS runs: module v1. highcard_100mb PARCS runs: module v2.
  The only v1 -> v2 change is the merge (into the largest partial result instead of a fresh
  dictionary); for natural_* the merge takes 0.02-0.06 ms, so v1 and v2 are equivalent there.
- highcard_100mb needed two infrastructure changes during the run, both in deploy/base:
  daemon memory 1Gi -> 2Gi (one worker holding ~6.6M distinct words: OutOfMemoryException),
  host memory -> 5Gi (receiving and merging the partial counts).
- summary.csv / raw.csv rebuilt from reports/*.json (bench.py --rebuild).
