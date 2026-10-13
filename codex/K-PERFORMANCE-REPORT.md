# K — Performance measurements

Measured 2026-10-08 on Linux 7.0.0-34-generic x86_64, Intel Core Ultra 9 288V,
8 logical CPUs, host RAM 30.48 GiB. Node24.19.0, .NET SDK10.0.401,
PostgreSQL17.6-alpine, Chrome 154.0.8037.57 headless. Docker development
Release-built API/Web; loopback nginx, no WAN/throttling; one sequential caller, no
concurrent-user capacity claim. P6 uses real PostgreSQL/Testcontainers + TestServer
HTTP in test build, so its absolute latency is not directly comparable to Docker P1–P5.

Reproduction: `release-check.sh`, `release-performance.mjs`, and
`Release_queue_1000_is_paginated_with_constant_query_count`. Raw values retained in
[evidence/k](evidence/k); no invented timings. Baseline production code was `47d6de3`;
final includes K snapshot/access/stylus fixes. Scenario seeds are isolated and reset.

Generated A4 PNG: 1190×1684, 288369 bytes, text on white. This
is a repeatable synthetic document, not noisy high-megapixel camera photography.
The tablet scenario separately uses JPEG4200×3000, EXIF6 →3000×4200, DPR2.
API GET p50/p95 use 20 warm samples after one discarded warmup, nearest-rank. Values
include response read/JSON decoding in Node. Upload samples are only the profile's
1/5/10 files (P1 n=1 cannot estimate a population percentile). Submit/image startup
and P5 total History load are single observations. No statistically rigorous SLO claim.

## P1–P5 baseline → final

| Profile / request | Baseline p50 / p95 ms | Final p50 / p95 ms | Final JSON bytes |
|---|---:|---:|---:|
| P1 1 photo / teacher GET | 8.61 / 19.44 | 11.11 / 14.73 | 1482 |
| P2 5 photos / teacher GET | 7.61 / 10.63 | 10.36 / 12.81 | 1779 |
| P3 10 photos / teacher GET | 7.80 / 12.18 | 9.58 / 13.47 | 2184 |
| P4 500 strokes / 50000 points / teacher GET | 124.65 / 142.65 | 144.07 / 188.62 | 3770703 |
| P5 20 reviewed attempts / teacher GET | 5273.42 / 5520.76 | 49.16 / 59.36 | 803259 |
| P5 20 reviewed attempts / student workspace GET | 5238.84 / 5328.28 | 55.16 / 71.87 | 843264 |

P5 has one newly uploaded photo per attempt, 10 strokes ×100 points per Review,
19 NeedsRevision + final Accepted, 20 feedback messages. All transitions use real
HTTP commands; valid under the current model. Attempts and annotations verified.

**K-P01 P1 FIXED**: base AsSingleQuery joined three sibling collections. For P5,
20×20×20 =8000 joined rows repeated large annotation JSON, despite only20 reviews.
Split collection reads eliminate the cross product. A RepeatableRead snapshot wraps
standalone aggregate loads; workspace/lesson readers reuse their existing snapshot.
Two interleaving-write tests prove consistent history. Lesson's collection load is
also split. This is a measured query fix, not a domain/state-machine refactor.

## Browser interaction and image loading

| Profile | Initial image ms (one load) | Page switches p50 / p95 ms (n) | Upload p50 / p95 ms (n) | Submit ms |
|---|---:|---:|---:|---:|
| P1 | 119.65 | N/A (one page) | 142.31 / 142.31 (1) | 127.97 |
| P2 | 71.36 | 66.70 / 74.55 (24) | 24.22 / 38.68 (5) | 23.80 |
| P3 | 62.40 | 66.67 / 70.02 (54) | 20.74 / 23.12 (10) | 18.01 |

P5 History with all20 images loaded: 5601.73 → **281.72 ms**.

P4 scripted drawing500 strokes: 15679.40 → 15662.10 ms total.
Final100-point stroke handler p50/p95: 0.80/1.50 ms;
wait for next animation frame p50/p95/max: 23.50/73.00/77.70 ms.
Long-task observations: 68, maximum 79.00 ms.
Review payload 3769083 bytes (near the4 MiB cap); save + authoritative refresh
1188.65 ms. Synthetic events are dispatched synchronously then one
frame per stroke; this is not physical pen input-to-pixel latency or true refresh FPS.

**K-P02 P2 OPEN**: dense annotation rendering has visible headroom limits. Every frame
redraws the retained canvas paths; no claim of60fps with500 strokes. Smaller per-page
annotation sets are a workaround. A retained/static layer cache is a future measured
optimization; no unsolicited editor redesign added. HTTP bytes can hit4 MiB before
100000-point Domain bound (serialization precision varies); clients show validation.

## Memory, listeners and object URLs

| Profile | JS heap before → after page switching, bytes | Event listeners before → after | Active Blob URLs / after unmount |
|---|---:|---:|---:|
| P1 | 3877668 → 3877508 | 196 → 196 | 1 / 0 |
| P2 | 4011584 → 5284360 | 196 → 209 | 1 / 0 |
| P3 | 4014576 → 5395312 | 196 → 209 | 1 / 0 |

CDP forces GC before samples; these are JS heap/listener counts, not decoded-image/GPU
or process RSS. P4 heap after save: 9035976 bytes;
P5 loaded History heap: 6580960 bytes. Post-run
Docker working memory: API227.8 MiB, PostgreSQL40.7 MiB (`memory.txt`); not peak RSS.
Repeated paging keeps one active original URL and navigation revokes it (browser assert).
Source audit: AbortController, revokeObjectURL, ResizeObserver disconnect, load/error/
resize listeners and RAF cancellation on cleanup. New React Profiler regression proves
100 pointermove samples do not commit React on each point; completed stroke does.
This short run does not prove absence of multi-hour or browser-specific memory leaks.

## P6 queue and N+1 audit

1000 Submitted works,1000 students in one authorized course, page2, real database.

| Page size | p50 / p95 ms | Response bytes | SQL reader commands |
|---|---:|---:|---:|
| 1 | 38.01 / 66.54 | 765 | 5 |
| 50 | 37.71 / 39.28 | 25609 | 5 |
| 200 | 37.70 / 39.77 | 101635 | 5 |

Queue query count is fixed5 for sizes1/50/200; no per-row N+1. Summary is over the
full filtered dataset. Pagination pass is functional; only local sequential latency
was measured. P1–P5 production HTTP query counts were not instrumented; source shows
fixed aggregate collection queries, with snapshot consistency regression coverage.

**K-P03 P2 OPEN**: adjacent teacher Lesson with1001 assignments measured
2011 SQL reader commands, 1166.20 ms, 794146 response bytes (one observation).
`PostgresWorkspaceQueries.Lesson` resolves content and availability inside its assignment
loop; Course resolves content per lesson. Splitting history does not remove this N+1.
Bound the pilot roster/course size; batch these lookups before a larger rollout.

## Proposed budgets (after measurement, not pre-existing acceptance criteria)

For a staging host comparable to this one, propose warm read p95≤100ms for P1–P3,
≤250ms for P4/P5;1000-work queue p95≤100ms at page50; photo page switch p95≤150ms.
These leave roughly2× local measured headroom for common reads. Require independent
staging/concurrent-user confirmation, with real camera files and agreed workload.
For drawing propose p95 next-frame wait≤33ms (30fps usability target); dense P4 does
**not** meet that proposed target. Do not retroactively label a newly chosen budget
an existing product requirement. Capacity/Safari/hardware acceptance remain NOT TESTED.
