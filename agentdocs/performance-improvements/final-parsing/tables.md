# Fresh Release comparison tables

Times are microseconds. Values are the median of two process medians; each process records nine batches. Ratios compare optimized with the named fixed baseline. Original historical measurements remain under `agentdocs/benchmark-results`.

## native

| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |
|---|---:|---:|---:|---:|---:|---|
| header / parse | 0.71 | 0.79 | 0.72 | 1.01× | 0.90× | 1.02×, 0.99× |
| header / debug | 0.92 | 0.97 | 0.88 | 0.96× | 0.91× | 0.98×, 0.95× |
| fields128 / parse | 50.99 | 54.65 | 48.45 | 0.95× | 0.89× | 0.93×, 0.97× |
| fields128 / debug | 58.10 | 62.49 | 55.35 | 0.95× | 0.89× | 0.92×, 0.98× |
| bytes1024 / parse | 46.57 | 55.29 | 48.16 | 1.03× | 0.87× | 1.02×, 1.05× |
| bytes1024 / debug | 78.94 | 86.81 | 79.52 | 1.01× | 0.92× | 1.02×, 1.00× |
| plain128 / parse | 52.58 | 61.06 | 53.40 | 1.02× | 0.87× | 1.02×, 1.02× |
| plain128 / debug | 73.41 | 100.39 | 76.57 | 1.04× | 0.76× | 1.04×, 1.05× |
| if128 / parse | — | 352.49 | 79.96 | —× | 0.23× |  |
| if128 / debug | — | 385.13 | 103.76 | —× | 0.27× |  |
| switch128 / parse | — | 341.31 | 78.35 | —× | 0.23× |  |
| switch128 / debug | — | 396.04 | 103.83 | —× | 0.26× |  |
| wideplain128 / parse | 50.53 | 55.41 | 50.80 | 1.01× | 0.92× | 1.00×, 1.01× |
| wideplain128 / debug | 60.09 | 63.47 | 57.69 | 0.96× | 0.91× | 0.95×, 0.97× |
| wideif128 / parse | — | 352.39 | 54.91 | —× | 0.16× |  |
| wideif128 / debug | — | 352.27 | 61.07 | —× | 0.17× |  |
| mixedplain128 / parse | 53.38 | 60.48 | 53.32 | 1.00× | 0.88× | 0.99×, 1.01× |
| mixedplain128 / debug | 72.35 | 104.37 | 77.35 | 1.07× | 0.74× | 1.09×, 1.05× |
| mixedswitch128 / parse | — | 466.77 | 82.09 | —× | 0.18× |  |
| mixedswitch128 / debug | — | 520.40 | 109.46 | —× | 0.21× |  |
| nestedif128 / parse | — | 580.30 | 88.62 | —× | 0.15× |  |
| nestedif128 / debug | — | 609.76 | 116.50 | —× | 0.19× |  |

### Conditional price

| Pair / operation | Feature conditional/plain | Optimized conditional/plain |
|---|---:|---:|
| if128/plain128 / parse | 5.77× | 1.50× |
| if128/plain128 / debug | 3.84× | 1.36× |
| switch128/plain128 / parse | 5.59× | 1.47× |
| switch128/plain128 / debug | 3.94× | 1.36× |
| wideif128/wideplain128 / parse | 6.36× | 1.08× |
| wideif128/wideplain128 / debug | 5.55× | 1.06× |
| mixedswitch128/mixedplain128 / parse | 7.72× | 1.54× |
| mixedswitch128/mixedplain128 / debug | 4.99× | 1.42× |
| nestedif128/mixedplain128 / parse | 9.59× | 1.66× |
| nestedif128/mixedplain128 / debug | 5.84× | 1.51× |

## js

| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |
|---|---:|---:|---:|---:|---:|---|
| header / parseCore | 9.22 | 11.47 | 9.49 | 1.03× | 0.83× | 1.04×, 1.02× |
| header / publicParse | 263040.90 | 271443.15 | 870.83 | 0.00× | 0.00× | 0.00×, 0.00× |
| header / publicDebug | 1257.23 | 1447.65 | 793.38 | 0.63× | 0.55× | 0.65×, 0.62× |
| header / compiledParse | — | — | 127.61 | —× | —× |  |
| header / compiledDebug | — | — | 129.14 | —× | —× |  |
| fields128 / parseCore | 317.59 | 383.37 | 323.26 | 1.02× | 0.84× | 1.02×, 1.02× |
| fields128 / publicParse | 305814.90 | 314170.95 | 11937.00 | 0.04× | 0.04× | 0.04×, 0.04× |
| fields128 / publicDebug | 12930.18 | 13508.25 | 12830.34 | 0.99× | 0.95× | 1.00×, 0.99× |
| fields128 / compiledParse | — | — | 504.45 | —× | —× |  |
| fields128 / compiledDebug | — | — | 1321.84 | —× | —× |  |
| bytes1024 / parseCore | 709.98 | 919.43 | 725.57 | 1.02× | 0.79× | 1.03×, 1.01× |
| bytes1024 / publicParse | 276229.65 | 285936.80 | 1892.27 | 0.01× | 0.01× | 0.01×, 0.01× |
| bytes1024 / publicDebug | 6861.38 | 7286.18 | 5688.79 | 0.83× | 0.78× | 0.84×, 0.82× |
| bytes1024 / compiledParse | — | — | 1136.79 | —× | —× |  |
| bytes1024 / compiledDebug | — | — | 5502.42 | —× | —× |  |
| plain128 / parseCore | 853.12 | 1085.16 | 877.16 | 1.03× | 0.81× | 1.03×, 1.02× |
| plain128 / publicParse | 280370.85 | 291534.00 | 2539.88 | 0.01× | 0.01× | 0.01×, 0.01× |
| plain128 / publicDebug | 5292.03 | 6023.97 | 4682.71 | 0.88× | 0.78× | 0.89×, 0.88× |
| plain128 / compiledParse | — | — | 1485.03 | —× | —× |  |
| plain128 / compiledDebug | — | — | 3736.27 | —× | —× |  |
| if128 / parseCore | — | 5045.45 | 1200.65 | —× | 0.24× |  |
| if128 / publicParse | — | 308629.35 | 3378.23 | —× | 0.01× |  |
| if128 / publicDebug | — | 11055.48 | 5478.85 | —× | 0.50× |  |
| if128 / compiledParse | — | — | 1836.04 | —× | —× |  |
| if128 / compiledDebug | — | — | 4157.03 | —× | —× |  |
| switch128 / parseCore | — | 5143.81 | 1210.21 | —× | 0.24× |  |
| switch128 / publicParse | — | 308009.90 | 3427.90 | —× | 0.01× |  |
| switch128 / publicDebug | — | 11552.41 | 5586.90 | —× | 0.48× |  |
| switch128 / compiledParse | — | — | 1880.92 | —× | —× |  |
| switch128 / compiledDebug | — | — | 4250.32 | —× | —× |  |
| wideplain128 / parseCore | 318.37 | 396.66 | 327.29 | 1.03× | 0.83× | 1.04×, 1.02× |
| wideplain128 / publicParse | 305561.40 | 313103.70 | 12132.65 | 0.04× | 0.04× | 0.04×, 0.04× |
| wideplain128 / publicDebug | 13063.90 | 13773.24 | 12804.10 | 0.98× | 0.93× | 1.00×, 0.96× |
| wideplain128 / compiledParse | — | — | 512.42 | —× | —× |  |
| wideplain128 / compiledDebug | — | — | 1394.73 | —× | —× |  |
| wideif128 / parseCore | — | 3700.02 | 376.31 | —× | 0.10× |  |
| wideif128 / publicParse | — | 332175.90 | 12982.39 | —× | 0.04× |  |
| wideif128 / publicDebug | — | 17421.20 | 13545.98 | —× | 0.78× |  |
| wideif128 / compiledParse | — | — | 591.58 | —× | —× |  |
| wideif128 / compiledDebug | — | — | 1452.20 | —× | —× |  |
| mixedplain128 / parseCore | 853.52 | 1087.07 | 876.17 | 1.03× | 0.81× | 1.05×, 1.00× |
| mixedplain128 / publicParse | 280899.80 | 291031.70 | 2464.61 | 0.01× | 0.01× | 0.01×, 0.01× |
| mixedplain128 / publicDebug | 5237.08 | 5953.94 | 4613.81 | 0.88× | 0.77× | 0.88×, 0.88× |
| mixedplain128 / compiledParse | — | — | 1524.73 | —× | —× |  |
| mixedplain128 / compiledDebug | — | — | 3732.57 | —× | —× |  |
| mixedswitch128 / parseCore | — | 7033.37 | 1222.09 | —× | 0.17× |  |
| mixedswitch128 / publicParse | — | 318617.10 | 3956.56 | —× | 0.01× |  |
| mixedswitch128 / publicDebug | — | 13680.10 | 6153.20 | —× | 0.45× |  |
| mixedswitch128 / compiledParse | — | — | 1886.09 | —× | —× |  |
| mixedswitch128 / compiledDebug | — | — | 4163.57 | —× | —× |  |
| nestedif128 / parseCore | — | 8469.60 | 1325.49 | —× | 0.16× |  |
| nestedif128 / publicParse | — | 320064.65 | 4183.32 | —× | 0.01× |  |
| nestedif128 / publicDebug | — | 15521.65 | 6228.97 | —× | 0.40× |  |
| nestedif128 / compiledParse | — | — | 2009.44 | —× | —× |  |
| nestedif128 / compiledDebug | — | — | 4283.29 | —× | —× |  |

### Conditional price

| Pair / operation | Feature conditional/plain | Optimized conditional/plain |
|---|---:|---:|
| if128/plain128 / parseCore | 4.65× | 1.37× |
| if128/plain128 / compiledParse | — | 1.24× |
| if128/plain128 / compiledDebug | — | 1.11× |
| switch128/plain128 / parseCore | 4.74× | 1.38× |
| switch128/plain128 / compiledParse | — | 1.27× |
| switch128/plain128 / compiledDebug | — | 1.14× |
| wideif128/wideplain128 / parseCore | 9.33× | 1.15× |
| wideif128/wideplain128 / compiledParse | — | 1.15× |
| wideif128/wideplain128 / compiledDebug | — | 1.04× |
| mixedswitch128/mixedplain128 / parseCore | 6.47× | 1.39× |
| mixedswitch128/mixedplain128 / compiledParse | — | 1.24× |
| mixedswitch128/mixedplain128 / compiledDebug | — | 1.12× |
| nestedif128/mixedplain128 / parseCore | 7.79× | 1.51× |
| nestedif128/mixedplain128 / compiledParse | — | 1.32× |
| nestedif128/mixedplain128 / compiledDebug | — | 1.15× |

## Native allocated bytes per operation

| Scenario / operation | Main | Feature | Optimized |
|---|---:|---:|---:|
| header/parse | 1840 | 2128 | 1848 |
| header/debug | 2408 | 2696 | 2456 |
| fields128/parse | 38207 | 42533 | 38130 |
| fields128/debug | 65139 | 69403 | 66257 |
| bytes1024/parse | 92408 | 125368 | 91904 |
| bytes1024/debug | 256344 | 289304 | 255864 |
| plain128/parse | 84146 | 120178 | 83641 |
| plain128/debug | 177402 | 245404 | 192512 |
| if128/parse | — | 712495 | 103097 |
| if128/debug | — | 838272 | 211960 |
| switch128/parse | — | 708413 | 103097 |
| switch128/debug | — | 834189 | 211960 |
| wideplain128/parse | 39468 | 43890 | 39465 |
| wideplain128/debug | 78835 | 83250 | 79751 |
| wideif128/parse | — | 235748 | 40661 |
| wideif128/debug | — | 274771 | 80955 |
| mixedplain128/parse | 84146 | 120178 | 83642 |
| mixedplain128/debug | 177402 | 245405 | 192512 |
| mixedswitch128/parse | — | 1057507 | 105156 |
| mixedswitch128/debug | — | 1183075 | 214037 |
| nestedif128/parse | — | 1327796 | 106184 |
| nestedif128/debug | — | 1453337 | 215068 |
