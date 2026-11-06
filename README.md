# PARCS word count (Cloud Computing, lab 1, variant 4.1)

Parallel word-frequency counting on [parcs8](https://github.com/alexeybogusevich/parcs8)
(PARCS on .NET 10 and Kubernetes), deployed on Google Kubernetes Engine with a master node and
four worker nodes, compared with a sequential baseline that runs the same counting code.

- **Algorithm:**
  - The main module splits the input file into N contiguous byte ranges, one per PARCS point.
  - Each worker reads **only its own range** from the shared storage and counts the words that *start* in it.
  - Workers send back their partial counts, and the main module merges them.
  - A word is a maximal run of ASCII letters or digits, lowercased.
- **Timing:**
  - Each job starts its N points once.
  - It then runs one warmup round and five measured rounds over the same points.
  - Point start-up is reported separately from the per-round time.

Report: [report/Звіт_ЛР1_Лейцишин.pdf](report/Звіт_ЛР1_Лейцишин.pdf).

## Layout

```
src/WordCount.Core/              shared counting code (ranges, boundary rule, merge, output format)
modules/Parcs.Modules.WordCount/ PARCS module: MainModule (split/dispatch/merge), WorkerModule
tools/WordCount.Seq/             sequential baseline (same core, one thread)
docker/                          parcs8.Dockerfile (daemon/host/portal from source),
                                 wordcount-seq.Dockerfile, cloudbuild.yaml
patches/                         fixes applied to the parcs8 sources before building
deploy/base, deploy/gcp          Kubernetes manifests (kustomize), NFS-Ganesha Helm values
deploy/jobs/                     dataset generation and sequential baseline Jobs
scripts/datagen.py               deterministic dataset generator
results/gcp/                     measurements: raw and summary CSV, per-job reports, figures
report/                          the report (PDF) and the cluster screenshots
```

## parcs8 sources

```bash
git clone https://github.com/alexeybogusevich/parcs8 third_party/parcs8
git -C third_party/parcs8 checkout d35d29f948cb1849a070967e681bc0df7f25d779
git -C third_party/parcs8 apply ../../patches/0001-optional-elasticsearch-logging.patch
```

## Build and deploy

Images are built from `docker/parcs8.Dockerfile` (targets `daemon`, `host`, `portal`; build
context: the parcs8 source tree) and `docker/wordcount-seq.Dockerfile` (context: this repository),
for example with Cloud Build and `docker/cloudbuild.yaml`. The module is built with
`dotnet publish modules/Parcs.Modules.WordCount -c Release`. It needs `Parcs.Net` 10.1.0, which
is packed from `third_party/parcs8/src/Parcs.Net` because nuget.org does not have it.

The full command sequence (project, images, cluster with master + worker-1..4, NFS storage,
parcs8, datasets, job submission through the REST API, sequential baseline, teardown) is in
section 6 of the report.

## Problems found in upstream parcs8 and how they are fixed

| Problem | Fix |
|---|---|
| All projects reference NuGet `Parcs.Net` 10.1.0, which is not published (nuget.org stops at 10.0.0) | `docker/parcs8.Dockerfile` packs `src/Parcs.Net` into a local feed before restoring |
| `dotnet pack` of Parcs.Net fails with NU5026 (`GeneratePackageOnBuild=True`) | pack with `-p:GeneratePackageOnBuild=false` |
| The GCP manifest points at images in the author's project; the published images lack the fixes below | images built from source into our own Artifact Registry |
| Upstream manifests: PVC selectors match no PV, a missing Elasticsearch claim, elastic-mode (Pub/Sub, KEDA) assumptions | own manifests (`deploy/`) for the static mode with an explicit ReadWriteMany volume |
| .NET 8+ ASP.NET images listen on 8080 while the Services target port 80 | `ASPNETCORE_URLS=http://+:80` |
| `postgres:latest` is now 18, which changed the data directory layout | pinned `postgres:16` |
| Elasticsearch is a hard dependency used only for logging (the portal crashes without it) | `patches/0001`: an empty `Elasticsearch__BaseUrl` disables it; logs go to the console |
| The host migrates its database once at startup without retrying | startup/liveness/readiness probes on `/health` |
| The Job API needs an uploaded input file and rejects bodies over 30,000,000 bytes | datasets live on the shared storage; the job takes an `InputPath` argument |
| No ReadWriteMany storage on GKE by default; the claim must fit the NFS disk | NFS-Ganesha Helm chart 1.8.0 on the master node, 10 GiB claim |
| A single worker or the host runs out of memory on a vocabulary of 6.9M distinct words | daemon limit 2 GiB (same as the baseline), host 5 GiB, merge into the largest partial result |
