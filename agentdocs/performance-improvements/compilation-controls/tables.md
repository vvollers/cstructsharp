# Fresh Release comparison tables

Times are microseconds. Values are the median of two process medians; each process records nine batches. Ratios compare optimized with the named fixed baseline. Original historical measurements remain under `agentdocs/benchmark-results`.

## native

| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |
|---|---:|---:|---:|---:|---:|---|
| header / compile | 55.51 | 68.15 | 29.42 | 0.53× | 0.43× | 0.54×, 0.52× |
| fields128 / compile | 598.18 | 646.35 | 598.66 | 1.00× | 0.93× | 1.00×, 1.00× |
| wideplain128 / compile | 605.16 | 651.26 | 616.36 | 1.02× | 0.95× | 1.03×, 1.01× |
| wideif128 / compile | — | 688.56 | 664.57 | —× | 0.97× |  |

### Conditional price

| Pair / operation | Feature conditional/plain | Optimized conditional/plain |
|---|---:|---:|
| wideif128/wideplain128 / compile | 1.06× | 1.08× |

## js

| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |
|---|---:|---:|---:|---:|---:|---|
| header / compile | 755.14 | 855.16 | 464.45 | 0.62× | 0.54× | 0.61×, 0.62× |
| fields128 / compile | 11142.80 | 11527.60 | 11358.67 | 1.02× | 0.99× | 1.01×, 1.03× |
| wideplain128 / compile | 11498.04 | 11843.66 | 11496.22 | 1.00× | 0.97× | 0.99×, 1.01× |
| wideif128 / compile | — | 12436.05 | 12250.97 | —× | 0.99× |  |

### Conditional price

| Pair / operation | Feature conditional/plain | Optimized conditional/plain |
|---|---:|---:|
| wideif128/wideplain128 / compile | 1.05× | 1.07× |

## Native allocated bytes per operation

| Scenario / operation | Main | Feature | Optimized |
|---|---:|---:|---:|
| header/compile | 94224 | 135976 | 36376 |
| fields128/compile | 306456 | 354208 | 264608 |
| wideplain128/compile | 310144 | 357944 | 268424 |
| wideif128/compile | — | 388936 | 340360 |
