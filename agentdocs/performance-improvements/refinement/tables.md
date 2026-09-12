# Fresh Release comparison tables

Times are microseconds. Values are the median of two process medians; each process records nine batches. Ratios compare optimized with the named fixed baseline. Original historical measurements remain under `agentdocs/benchmark-results`.

## native

| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |
|---|---:|---:|---:|---:|---:|---|
| header / compile | 48.20 | 58.44 | 24.99 | 0.52× | 0.43× | 0.52×, 0.52× |
| fields128 / compile | 504.21 | 522.77 | 485.13 | 0.96× | 0.93× | 1.02×, 0.93× |
| bytes1024 / compile | 46.63 | 57.25 | 23.47 | 0.50× | 0.41× | 0.49×, 0.51× |
| nested16 / compile | 61.94 | 73.82 | 39.16 | 0.63× | 0.53× | 0.62×, 0.64× |
| dynamic32 / compile | 48.84 | 60.06 | 25.69 | 0.53× | 0.43× | 0.53×, 0.53× |
| text64 / compile | 49.82 | 60.99 | 27.02 | 0.54× | 0.44× | 0.54×, 0.54× |
| plain1 / compile | 60.96 | 73.74 | 39.13 | 0.64× | 0.53× | 0.64×, 0.64× |
| if1 / compile | — | 91.48 | 56.49 | —× | 0.62× |  |
| switch1 / compile | — | 95.62 | 60.05 | —× | 0.63× |  |
| plain128 / compile | 61.58 | 73.78 | 38.97 | 0.63× | 0.53× | 0.64×, 0.63× |
| if128 / compile | — | 90.58 | 56.84 | —× | 0.63× |  |
| switch128 / compile | — | 93.54 | 60.33 | —× | 0.64× |  |
| wideplain8 / compile | 70.28 | 80.51 | 46.19 | 0.66× | 0.57× | 0.66×, 0.66× |
| wideif8 / compile | — | 98.27 | 65.42 | —× | 0.67× |  |
| wideplain32 / compile | 157.94 | 168.23 | 136.29 | 0.86× | 0.81× | 0.87×, 0.86× |
| wideif32 / compile | — | 188.55 | 158.64 | —× | 0.84× |  |
| wideplain128 / compile | 513.17 | 525.52 | 499.90 | 0.97× | 0.95× | 0.99×, 0.96× |
| wideif128 / compile | — | 539.88 | 538.07 | —× | 1.00× |  |
| mixedplain128 / compile | 61.60 | 73.14 | 39.18 | 0.64× | 0.54× | 0.64×, 0.63× |
| mixedswitch128 / compile | — | 121.18 | 85.51 | —× | 0.71× |  |
| nestedif128 / compile | — | 123.94 | 91.63 | —× | 0.74× |  |

### Conditional price

| Pair / operation | Feature conditional/plain | Optimized conditional/plain |
|---|---:|---:|
| if1/plain1 / compile | 1.24× | 1.44× |
| switch1/plain1 / compile | 1.30× | 1.53× |
| if128/plain128 / compile | 1.23× | 1.46× |
| switch128/plain128 / compile | 1.27× | 1.55× |
| wideif8/wideplain8 / compile | 1.22× | 1.42× |
| wideif32/wideplain32 / compile | 1.12× | 1.16× |
| wideif128/wideplain128 / compile | 1.03× | 1.08× |
| mixedswitch128/mixedplain128 / compile | 1.66× | 2.18× |
| nestedif128/mixedplain128 / compile | 1.69× | 2.34× |

## js

| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |
|---|---:|---:|---:|---:|---:|---|
| header / compile | 754.94 | 855.63 | 461.84 | 0.61× | 0.54× | 0.62×, 0.61× |
| fields128 / compile | 9811.41 | 10142.47 | 10155.07 | 1.04× | 1.00× | 1.04×, 1.03× |
| bytes1024 / compile | 821.59 | 955.35 | 527.29 | 0.64× | 0.55× | 0.65×, 0.64× |
| nested16 / compile | 1158.68 | 1313.99 | 857.11 | 0.74× | 0.65× | 0.74×, 0.74× |
| dynamic32 / compile | 839.70 | 1001.19 | 546.75 | 0.65× | 0.55× | 0.67×, 0.63× |
| text64 / compile | 874.79 | 1023.60 | 577.41 | 0.66× | 0.56× | 0.67×, 0.65× |
| plain1 / compile | 1124.96 | 1267.99 | 828.29 | 0.74× | 0.65× | 0.75×, 0.73× |
| if1 / compile | — | 1621.94 | 1210.11 | —× | 0.75× |  |
| switch1 / compile | — | 1699.81 | 1467.10 | —× | 0.86× |  |
| plain128 / compile | 1126.66 | 1276.74 | 947.73 | 0.84× | 0.74× | 0.97×, 0.74× |
| if128 / compile | — | 1616.82 | 1368.44 | —× | 0.85× |  |
| switch128 / compile | — | 1693.24 | 1465.76 | —× | 0.87× |  |
| wideplain8 / compile | 1281.21 | 1416.31 | 1128.97 | 0.88× | 0.80× | 1.02×, 0.77× |
| wideif8 / compile | — | 1767.63 | 1561.09 | —× | 0.88× |  |
| wideplain32 / compile | 3040.61 | 3153.05 | 3241.89 | 1.07× | 1.03× | 1.27×, 0.92× |
| wideif32 / compile | — | 3560.24 | 3718.52 | —× | 1.04× |  |
| wideplain128 / compile | 9914.64 | 10180.09 | 11585.38 | 1.17× | 1.14× | 1.38×, 1.01× |
| wideif128 / compile | — | 10742.24 | 12291.22 | —× | 1.14× |  |
| mixedplain128 / compile | 1129.66 | 1284.91 | 944.17 | 0.84× | 0.73× | 0.97×, 0.74× |
| mixedswitch128 / compile | — | 2171.06 | 2004.54 | —× | 0.92× |  |
| nestedif128 / compile | — | 2299.11 | 2153.34 | —× | 0.94× |  |

### Conditional price

| Pair / operation | Feature conditional/plain | Optimized conditional/plain |
|---|---:|---:|
| if1/plain1 / compile | 1.28× | 1.46× |
| switch1/plain1 / compile | 1.34× | 1.77× |
| if128/plain128 / compile | 1.27× | 1.44× |
| switch128/plain128 / compile | 1.33× | 1.55× |
| wideif8/wideplain8 / compile | 1.25× | 1.38× |
| wideif32/wideplain32 / compile | 1.13× | 1.15× |
| wideif128/wideplain128 / compile | 1.06× | 1.06× |
| mixedswitch128/mixedplain128 / compile | 1.69× | 2.12× |
| nestedif128/mixedplain128 / compile | 1.79× | 2.28× |

## Native allocated bytes per operation

| Scenario / operation | Main | Feature | Optimized |
|---|---:|---:|---:|
| header/compile | 94224 | 136004 | 36416 |
| fields128/compile | 306456 | 354236 | 264648 |
| bytes1024/compile | 93664 | 135492 | 34720 |
| nested16/compile | 100352 | 142852 | 42536 |
| dynamic32/compile | 93976 | 135852 | 36184 |
| text64/compile | 95216 | 137092 | 36400 |
| plain1/compile | 100304 | 142804 | 42528 |
| if1/compile | — | 148620 | 50208 |
| switch1/compile | — | 153948 | 55728 |
| plain128/compile | 100384 | 142884 | 42608 |
| if128/compile | — | 148700 | 50288 |
| switch128/compile | — | 154028 | 55808 |
| wideplain8/compile | 104728 | 146796 | 47688 |
| wideif8/compile | — | 153788 | 58552 |
| wideplain32/compile | 145296 | 188516 | 91328 |
| wideif32/compile | — | 200308 | 113696 |
| wideplain128/compile | 310144 | 357972 | 268464 |
| wideif128/compile | — | 388964 | 340464 |
| mixedplain128/compile | 100384 | 142884 | 42608 |
| mixedswitch128/compile | — | 172124 | 71680 |
| nestedif128/compile | — | 164420 | 70048 |
