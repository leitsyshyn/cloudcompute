# PARCS word count (Cloud Computing, lab 1, variant 4.1)

Parallel word-frequency counting on [parcs8](https://github.com/alexeybogusevich/parcs8)
(PARCS on .NET 10 and Kubernetes), with a sequential baseline that runs the same counting code.

- **Algorithm:**
  - The main module splits the input file into N contiguous byte ranges, one per PARCS point.
  - Each worker reads **only its own range** from the shared storage and counts the words that *start* in it.
  - Workers send back their partial counts, and the main module merges them.
  - Word semantics match the RPP lab (C++ `wf-benchmark`): a word is a maximal run of ASCII letters or digits, lowercased.
- **Timing:**
  - Each job starts its N points once.
  - It then runs warmup rounds and measured rounds over the same points.
  - Point start-up (`SetupMs`) is reported separately from the per-round time.

## Layout

```
src/WordCount.Core/              shared counting code (ranges, boundary rule, merge, output format)
modules/Parcs.Modules.WordCount/ PARCS module: MainModule (split/dispatch/merge), WorkerModule
tools/WordCount.Seq/             sequential baseline (same core, one thread)
docker/parcs8.Dockerfile         builds parcs8 daemon/host/portal from source
deploy/local/                    kind cluster (master + worker-1..4) and parcs8 manifest
scripts/                         build, cluster up/down, REST client, validation, benchmark, plots
third_party/parcs8/              upstream sources (git-ignored, fetched at a pinned commit + patches/)
patches/                         local patches to parcs8, applied by scripts/fetch-parcs8.sh
.local/                          git-ignored runtime state: kubeconfig, storage, builds, results
```

## Run locally

Requirements: Docker Desktop (8 GB RAM), `kind`, `kubectl`, `python3`. No .NET SDK is needed,
because everything compiles inside containers.

```bash
scripts/fetch-parcs8.sh                 # upstream sources at the pinned commit + local patches
scripts/build-images.sh                 # parcs8/{sdk,daemon,host,portal}:local
scripts/build-module.sh                 # WordCount module + wordcount/seq:local
scripts/local-up.sh                     # kind cluster + parcs8 (static mode, 4 daemons)
export KUBECONFIG=$PWD/.local/kubeconfig

# datasets go to the shared storage (mounted into every node)
mkdir -p .local/storage/datasets && cp <input>.txt .local/storage/datasets/

MID=$(python3 scripts/parcs_client.py upload-module .local/build/module)
python3 scripts/parcs_client.py run --module-id $MID --points 4 \
    --input datasets/<input>.txt --repetitions 5 --warmup 1 --out .local/runs/demo
python3 scripts/validate.py --module-id $MID --datasets .local/storage/datasets --reference <dir>
python3 scripts/bench.py --module-id $MID --datasets <a>.txt,<b>.txt --out .local/results/local
python3 -m venv .local/venv && .local/venv/bin/pip install matplotlib
.local/venv/bin/python scripts/plot.py .local/results/local/summary.csv --title "local"

scripts/local-down.sh
```

- Host API (Swagger): http://127.0.0.1:5080/swagger
- Portal: http://127.0.0.1:5081
- Nodes and pods: `kubectl get nodes -L parcs-role,parcs-worker` and `kubectl get pods -o wide`

## Problems found in upstream parcs8 and how they are fixed here

| Problem | Fix |
|---|---|
| All projects reference NuGet `Parcs.Net` 10.1.0, which is not published (nuget.org stops at 10.0.0), so `dotnet restore` fails | `docker/parcs8.Dockerfile` packs `src/Parcs.Net` into a local feed before restoring |
| `dotnet pack` of Parcs.Net fails with NU5026, because the csproj sets `GeneratePackageOnBuild=True` | pack with `-p:GeneratePackageOnBuild=false` |
| Published images (`oleksiibohusevych/*`) are linux/amd64 only | build from source: native arm64 locally, amd64 for the cloud |
| `kube/deployment.local.yaml`: the PVC selectors (`app: parcs`) match no PV labels, and the Elasticsearch claim name does not exist, so pods stay Pending | own manifest: shared job storage as an explicit RWX PV, emptyDir for Postgres and Elasticsearch |
| .NET 8+ ASP.NET images listen on 8080, while the manifest's Services target port 80 | `ASPNETCORE_URLS=http://+:80` |
| `postgres:latest` is now 18, which changed the data directory layout | pinned `postgres:16` |
| Elasticsearch is a hard dependency used only for logging: the portal crashes at startup if it is not up yet, and it costs ~1 GB of RAM | `patches/0001-optional-elasticsearch-logging.patch`: an empty `Elasticsearch__BaseUrl` turns the Elasticsearch sink and health check off, logs go to the console (`kubectl logs`); Elasticsearch and Kibana are removed from the manifest |
| The host migrates its database once at startup without retrying: if Postgres is not ready yet, it stays alive but never listens | startup/liveness/readiness probes on `/health`, so Kubernetes restarts it until the migration succeeds |
| The manifest includes the AI-agent MCP server, whose image is not published | dropped (not needed) |
| The Job API requires at least one uploaded input file, and uploads over Kestrel's default 30,000,000-byte body limit fail with HTTP 400 ("Request body too large") | datasets are staged once on the shared storage, and the job takes an `InputPath` argument (a placeholder file is uploaded) |
