# Fresh Release comparison tables

Times are microseconds. Values are the median of two process medians; each process records nine batches. Ratios compare optimized with the named fixed baseline. Original historical measurements remain under `agentdocs/benchmark-results`.

## native

| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |
|---|---:|---:|---:|---:|---:|---|
| header / compile | 40.18 | 47.62 | 19.79 | 0.49× | 0.42× | 0.49×, 0.49× |
| header / parse | 0.43 | 0.46 | 0.40 | 0.94× | 0.86× | 0.92×, 0.95× |
| header / debug | 0.55 | 0.58 | 0.54 | 0.97× | 0.92× | 0.96×, 0.99× |
| header / span | 0.45 | 0.48 | 0.43 | 0.96× | 0.89× | 0.90×, 1.02× |
| fields128 / compile | 389.19 | 397.96 | 378.87 | 0.97× | 0.95× | 0.96×, 0.99× |
| fields128 / parse | 30.69 | 32.75 | 29.57 | 0.96× | 0.90× | 0.95×, 0.98× |
| fields128 / debug | 34.86 | 36.53 | 33.26 | 0.95× | 0.91× | 0.95×, 0.96× |
| fields128 / span | 31.18 | 33.05 | 30.14 | 0.97× | 0.91× | 0.94×, 0.99× |
| bytes1024 / compile | 38.36 | 47.14 | 18.74 | 0.49× | 0.40× | 0.49×, 0.49× |
| bytes1024 / parse | 25.22 | 30.10 | 25.75 | 1.02× | 0.86× | 1.04×, 1.00× |
| bytes1024 / debug | 42.31 | 48.05 | 43.32 | 1.02× | 0.90× | 1.04×, 1.01× |
| bytes1024 / span | 28.99 | 34.57 | 30.76 | 1.06× | 0.89× | 1.08×, 1.04× |
| nested16 / compile | 50.24 | 59.82 | 31.01 | 0.62× | 0.52× | 0.63×, 0.60× |
| nested16 / parse | 3.15 | 3.61 | 3.07 | 0.97× | 0.85× | 0.95×, 1.00× |
| nested16 / debug | 4.29 | 6.39 | 4.55 | 1.06× | 0.71× | 1.03×, 1.09× |
| nested16 / span | 3.38 | 3.83 | 3.33 | 0.98× | 0.87× | 0.96×, 1.01× |
| dynamic32 / compile | 40.03 | 49.13 | 20.48 | 0.51× | 0.42× | 0.52×, 0.51× |
| dynamic32 / parse | 2.13 | 2.43 | 1.43 | 0.67× | 0.59× | 0.66×, 0.68× |
| dynamic32 / debug | 3.06 | 3.31 | 2.23 | 0.73× | 0.67× | 0.72×, 0.74× |
| dynamic32 / span | 2.28 | 2.44 | 1.53 | 0.67× | 0.63× | 0.65×, 0.70× |
| text64 / compile | 41.41 | 49.57 | 22.11 | 0.53× | 0.45× | 0.53×, 0.54× |
| text64 / parse | 2.30 | 2.67 | 2.30 | 1.00× | 0.86× | 0.98×, 1.02× |
| text64 / debug | 3.57 | 3.93 | 3.58 | 1.00× | 0.91× | 0.98×, 1.02× |
| text64 / span | 2.62 | 2.99 | 2.58 | 0.99× | 0.86× | 0.98×, 0.99× |
| plain1 / compile | 50.38 | 58.78 | 31.05 | 0.62× | 0.53× | 0.62×, 0.62× |
| plain1 / parse | 0.62 | 0.69 | 0.57 | 0.92× | 0.83× | 0.91×, 0.93× |
| plain1 / debug | 0.75 | 0.94 | 0.73 | 0.97× | 0.78× | 0.96×, 0.99× |
| plain1 / span | 0.64 | 0.73 | 0.59 | 0.91× | 0.81× | 0.89×, 0.94× |
| if1 / compile | — | 72.88 | 45.88 | —× | 0.63× |  |
| if1 / parse | — | 2.18 | 0.70 | —× | 0.32× |  |
| if1 / debug | — | 2.61 | 0.87 | —× | 0.34× |  |
| if1 / span | — | 2.26 | 0.72 | —× | 0.32× |  |
| switch1 / compile | — | 75.78 | 48.64 | —× | 0.64× |  |
| switch1 / parse | — | 2.30 | 0.68 | —× | 0.30× |  |
| switch1 / debug | — | 2.41 | 0.85 | —× | 0.35× |  |
| switch1 / span | — | 2.37 | 0.70 | —× | 0.30× |  |
| plain128 / compile | 50.28 | 59.01 | 30.97 | 0.62× | 0.52× | 0.62×, 0.61× |
| plain128 / parse | 30.88 | 34.77 | 29.56 | 0.96× | 0.85× | 0.92×, 1.00× |
| plain128 / debug | 41.67 | 59.92 | 43.06 | 1.03× | 0.72× | 1.00×, 1.06× |
| plain128 / span | 32.42 | 37.00 | 32.01 | 0.99× | 0.87× | 0.94×, 1.03× |
| if128 / compile | — | 72.51 | 45.58 | —× | 0.63× |  |
| if128 / parse | — | 235.39 | 44.18 | —× | 0.19× |  |
| if128 / debug | — | 262.97 | 58.65 | —× | 0.22× |  |
| if128 / span | — | 232.75 | 45.70 | —× | 0.20× |  |
| switch128 / compile | — | 76.93 | 49.22 | —× | 0.64× |  |
| switch128 / parse | — | 230.44 | 43.41 | —× | 0.19× |  |
| switch128 / debug | — | 267.78 | 58.33 | —× | 0.22× |  |
| switch128 / span | — | 234.13 | 45.15 | —× | 0.19× |  |
| wideplain8 / compile | 56.23 | 64.01 | 37.16 | 0.66× | 0.58× | 0.65×, 0.67× |
| wideplain8 / parse | 1.01 | 1.08 | 0.97 | 0.96× | 0.90× | 0.93×, 1.00× |
| wideplain8 / debug | 1.36 | 1.44 | 1.28 | 0.94× | 0.89× | 0.93×, 0.95× |
| wideplain8 / span | 1.07 | 1.12 | 1.01 | 0.94× | 0.90× | 0.92×, 0.96× |
| wideif8 / compile | — | 78.14 | 52.24 | —× | 0.67× |  |
| wideif8 / parse | — | 5.63 | 1.21 | —× | 0.22× |  |
| wideif8 / debug | — | 6.14 | 1.56 | —× | 0.25× |  |
| wideif8 / span | — | 5.62 | 1.25 | —× | 0.22× |  |
| wideplain32 / compile | 122.53 | 129.56 | 102.99 | 0.84× | 0.79× | 0.84×, 0.84× |
| wideplain32 / parse | 4.34 | 4.55 | 3.99 | 0.92× | 0.88× | 0.91×, 0.94× |
| wideplain32 / debug | 5.55 | 5.74 | 5.01 | 0.90× | 0.87× | 0.89×, 0.92× |
| wideplain32 / span | 4.53 | 4.75 | 4.14 | 0.91× | 0.87× | 0.91×, 0.92× |
| wideif32 / compile | — | 144.06 | 126.66 | —× | 0.88× |  |
| wideif32 / parse | — | 25.47 | 4.55 | —× | 0.18× |  |
| wideif32 / debug | — | 27.30 | 5.57 | —× | 0.20× |  |
| wideif32 / span | — | 25.47 | 4.75 | —× | 0.19× |  |
| wideplain128 / compile | 385.36 | 396.33 | 375.01 | 0.97× | 0.95× | 0.99×, 0.96× |
| wideplain128 / parse | 31.21 | 32.92 | 30.07 | 0.96× | 0.91× | 0.95×, 0.98× |
| wideplain128 / debug | 36.22 | 37.36 | 33.87 | 0.93× | 0.91× | 0.93×, 0.94× |
| wideplain128 / span | 31.47 | 33.98 | 30.52 | 0.97× | 0.90× | 0.96×, 0.97× |
| wideif128 / compile | — | 417.57 | 428.77 | —× | 1.03× |  |
| wideif128 / parse | — | 214.80 | 31.90 | —× | 0.15× |  |
| wideif128 / debug | — | 221.08 | 35.98 | —× | 0.16× |  |
| wideif128 / span | — | 213.28 | 32.70 | —× | 0.15× |  |
| mixedplain128 / compile | 49.71 | 59.09 | 30.77 | 0.62× | 0.52× | 0.62×, 0.61× |
| mixedplain128 / parse | 30.17 | 34.89 | 29.80 | 0.99× | 0.85× | 0.96×, 1.01× |
| mixedplain128 / debug | 41.37 | 59.24 | 43.27 | 1.05× | 0.73× | 1.02×, 1.08× |
| mixedplain128 / span | 32.29 | 36.64 | 31.95 | 0.99× | 0.87× | 0.97×, 1.00× |
| mixedswitch128 / compile | — | 96.09 | 68.43 | —× | 0.71× |  |
| mixedswitch128 / parse | — | 307.63 | 47.74 | —× | 0.16× |  |
| mixedswitch128 / debug | — | 349.10 | 63.44 | —× | 0.18× |  |
| mixedswitch128 / span | — | 318.22 | 49.93 | —× | 0.16× |  |
| nestedif128 / compile | — | 97.33 | 71.80 | —× | 0.74× |  |
| nestedif128 / parse | — | 399.22 | 51.33 | —× | 0.13× |  |
| nestedif128 / debug | — | 410.72 | 68.10 | —× | 0.17× |  |
| nestedif128 / span | — | 384.12 | 54.24 | —× | 0.14× |  |

### Conditional price

| Pair / operation | Feature conditional/plain | Optimized conditional/plain |
|---|---:|---:|
| if1/plain1 / compile | 1.24× | 1.48× |
| if1/plain1 / parse | 3.16× | 1.22× |
| if1/plain1 / debug | 2.78× | 1.19× |
| switch1/plain1 / compile | 1.29× | 1.57× |
| switch1/plain1 / parse | 3.33× | 1.19× |
| switch1/plain1 / debug | 2.57× | 1.16× |
| if128/plain128 / compile | 1.23× | 1.47× |
| if128/plain128 / parse | 6.77× | 1.49× |
| if128/plain128 / debug | 4.39× | 1.36× |
| switch128/plain128 / compile | 1.30× | 1.59× |
| switch128/plain128 / parse | 6.63× | 1.47× |
| switch128/plain128 / debug | 4.47× | 1.35× |
| wideif8/wideplain8 / compile | 1.22× | 1.41× |
| wideif8/wideplain8 / parse | 5.20× | 1.25× |
| wideif8/wideplain8 / debug | 4.27× | 1.22× |
| wideif32/wideplain32 / compile | 1.11× | 1.23× |
| wideif32/wideplain32 / parse | 5.60× | 1.14× |
| wideif32/wideplain32 / debug | 4.76× | 1.11× |
| wideif128/wideplain128 / compile | 1.05× | 1.14× |
| wideif128/wideplain128 / parse | 6.52× | 1.06× |
| wideif128/wideplain128 / debug | 5.92× | 1.06× |
| mixedswitch128/mixedplain128 / compile | 1.63× | 2.22× |
| mixedswitch128/mixedplain128 / parse | 8.82× | 1.60× |
| mixedswitch128/mixedplain128 / debug | 5.89× | 1.47× |
| nestedif128/mixedplain128 / compile | 1.65× | 2.33× |
| nestedif128/mixedplain128 / parse | 11.44× | 1.72× |
| nestedif128/mixedplain128 / debug | 6.93× | 1.57× |

## js

| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |
|---|---:|---:|---:|---:|---:|---|
| header / compile | 750.66 | 847.91 | 458.88 | 0.61× | 0.54× | 0.61×, 0.61× |
| header / parseCore | 10.02 | 12.27 | 10.14 | 1.01× | 0.83× | 1.01×, 1.02× |
| header / parseJson | 19.77 | 22.66 | 20.01 | 1.01× | 0.88× | 1.01×, 1.02× |
| header / publicParse | 209747.80 | 211550.45 | 714.25 | 0.00× | 0.00× | 0.00×, 0.00× |
| header / publicDebug | 850.58 | 980.55 | 623.86 | 0.73× | 0.64× | 0.74×, 0.73× |
| header / compiledParse | — | — | 127.49 | —× | —× |  |
| header / compiledDebug | — | — | 132.70 | —× | —× |  |
| fields128 / compile | 8192.73 | 8540.05 | 8303.36 | 1.01× | 0.97× | 1.01×, 1.01× |
| fields128 / parseCore | 221.42 | 268.37 | 223.95 | 1.01× | 0.83× | 1.00×, 1.02× |
| fields128 / parseJson | 321.61 | 363.15 | 317.47 | 0.99× | 0.87× | 0.95×, 1.03× |
| fields128 / publicParse | 247936.30 | 248258.05 | 8826.50 | 0.04× | 0.04× | 0.03×, 0.04× |
| fields128 / publicDebug | 9524.21 | 9631.31 | 9155.05 | 0.96× | 0.95× | 0.93×, 0.99× |
| fields128 / compiledParse | — | — | 441.02 | —× | —× |  |
| fields128 / compiledDebug | — | — | 998.72 | —× | —× |  |
| bytes1024 / compile | 719.55 | 826.41 | 426.80 | 0.59× | 0.52× | 0.58×, 0.60× |
| bytes1024 / parseCore | 472.61 | 599.05 | 472.36 | 1.00× | 0.79× | 0.97×, 1.03× |
| bytes1024 / parseJson | 741.35 | 851.56 | 734.86 | 0.99× | 0.86× | 0.96×, 1.03× |
| bytes1024 / publicParse | 221804.40 | 222774.65 | 1398.77 | 0.01× | 0.01× | 0.01×, 0.01× |
| bytes1024 / publicDebug | 4613.58 | 4906.00 | 3798.26 | 0.82× | 0.77× | 0.81×, 0.84× |
| bytes1024 / compiledParse | — | — | 870.35 | —× | —× |  |
| bytes1024 / compiledDebug | — | — | 3799.51 | —× | —× |  |
| nested16 / compile | 963.78 | 1094.19 | 712.23 | 0.74× | 0.65× | 0.74×, 0.74× |
| nested16 / parseCore | 69.84 | 87.48 | 70.81 | 1.01× | 0.81× | 1.00×, 1.03× |
| nested16 / parseJson | 115.29 | 134.05 | 114.65 | 0.99× | 0.86× | 0.98×, 1.00× |
| nested16 / publicParse | 212626.25 | 216100.50 | 1040.92 | 0.00× | 0.00× | 0.00×, 0.00× |
| nested16 / publicDebug | 1313.79 | 1511.51 | 1040.32 | 0.79× | 0.69× | 0.79×, 0.80× |
| nested16 / compiledParse | — | — | 236.41 | —× | —× |  |
| nested16 / compiledDebug | — | — | 375.08 | —× | —× |  |
| dynamic32 / compile | 726.98 | 838.97 | 458.51 | 0.63× | 0.55× | 0.62×, 0.64× |
| dynamic32 / parseCore | 40.16 | 46.12 | 30.61 | 0.76× | 0.66× | 0.74×, 0.78× |
| dynamic32 / parseJson | 62.01 | 67.75 | 51.63 | 0.83× | 0.76× | 0.81×, 0.86× |
| dynamic32 / publicParse | 211865.00 | 212664.10 | 684.86 | 0.00× | 0.00× | 0.00×, 0.00× |
| dynamic32 / publicDebug | 1024.96 | 1163.33 | 682.71 | 0.67× | 0.59× | 0.66×, 0.68× |
| dynamic32 / compiledParse | — | — | 152.85 | —× | —× |  |
| dynamic32 / compiledDebug | — | — | 274.81 | —× | —× |  |
| text64 / compile | 756.43 | 887.86 | 478.37 | 0.63× | 0.54× | 0.63×, 0.64× |
| text64 / parseCore | 51.83 | 61.99 | 49.91 | 0.96× | 0.81× | 0.96×, 0.97× |
| text64 / parseJson | 62.51 | 72.58 | 60.64 | 0.97× | 0.84× | 0.96×, 0.98× |
| text64 / publicParse | 218696.45 | 215013.90 | 708.54 | 0.00× | 0.00× | 0.00×, 0.00× |
| text64 / publicDebug | 1112.71 | 1272.31 | 773.07 | 0.69× | 0.61× | 0.70×, 0.69× |
| text64 / compiledParse | — | — | 170.29 | —× | —× |  |
| text64 / compiledDebug | — | — | 358.30 | —× | —× |  |
| plain1 / compile | 968.68 | 1109.24 | 701.02 | 0.72× | 0.63× | 0.72×, 0.72× |
| plain1 / parseCore | 17.02 | 20.89 | 15.32 | 0.90× | 0.73× | 0.91×, 0.89× |
| plain1 / parseJson | 31.20 | 35.11 | 29.58 | 0.95× | 0.84× | 0.95×, 0.94× |
| plain1 / publicParse | 212273.00 | 213560.55 | 878.92 | 0.00× | 0.00× | 0.00×, 0.00× |
| plain1 / publicDebug | 1078.95 | 1224.49 | 806.81 | 0.75× | 0.66× | 0.74×, 0.75× |
| plain1 / compiledParse | — | — | 128.81 | —× | —× |  |
| plain1 / compiledDebug | — | — | 141.82 | —× | —× |  |
| if1 / compile | — | 1361.36 | 1056.04 | —× | 0.78× |  |
| if1 / parseCore | — | 50.35 | 19.09 | —× | 0.38× |  |
| if1 / parseJson | — | 65.89 | 33.75 | —× | 0.51× |  |
| if1 / publicParse | — | 216075.90 | 1234.91 | —× | 0.01× |  |
| if1 / publicDebug | — | 1551.82 | 1144.00 | —× | 0.74× |  |
| if1 / compiledParse | — | — | 132.12 | —× | —× |  |
| if1 / compiledDebug | — | — | 146.79 | —× | —× |  |
| switch1 / compile | — | 1451.03 | 1115.28 | —× | 0.77× |  |
| switch1 / parseCore | — | 51.68 | 19.18 | —× | 0.37× |  |
| switch1 / parseJson | — | 67.18 | 33.61 | —× | 0.50× |  |
| switch1 / publicParse | — | 217227.65 | 1297.59 | —× | 0.01× |  |
| switch1 / publicDebug | — | 1665.17 | 1218.99 | —× | 0.73× |  |
| switch1 / compiledParse | — | — | 131.42 | —× | —× |  |
| switch1 / compiledDebug | — | — | 140.21 | —× | —× |  |
| plain128 / compile | 964.23 | 1118.56 | 702.90 | 0.73× | 0.63× | 0.72×, 0.74× |
| plain128 / parseCore | 617.87 | 799.35 | 621.83 | 1.01× | 0.78× | 1.00×, 1.02× |
| plain128 / parseJson | 936.33 | 1132.32 | 942.15 | 1.01× | 0.83× | 0.99×, 1.02× |
| plain128 / publicParse | 221072.85 | 226631.85 | 1870.42 | 0.01× | 0.01× | 0.01×, 0.01× |
| plain128 / publicDebug | 3681.94 | 4472.37 | 3223.78 | 0.88× | 0.72× | 0.87×, 0.88× |
| plain128 / compiledParse | — | — | 1123.58 | —× | —× |  |
| plain128 / compiledDebug | — | — | 2674.36 | —× | —× |  |
| if128 / compile | — | 1395.81 | 1026.01 | —× | 0.74× |  |
| if128 / parseCore | — | 4140.67 | 876.49 | —× | 0.21× |  |
| if128 / parseJson | — | 4633.82 | 1210.60 | —× | 0.26× |  |
| if128 / publicParse | — | 242114.75 | 2481.14 | —× | 0.01× |  |
| if128 / publicDebug | — | 8774.96 | 3879.34 | —× | 0.44× |  |
| if128 / compiledParse | — | — | 1421.80 | —× | —× |  |
| if128 / compiledDebug | — | — | 3039.69 | —× | —× |  |
| switch128 / compile | — | 1470.43 | 1103.39 | —× | 0.75× |  |
| switch128 / parseCore | — | 4205.43 | 877.89 | —× | 0.21× |  |
| switch128 / parseJson | — | 4764.79 | 1210.24 | —× | 0.25× |  |
| switch128 / publicParse | — | 240293.25 | 2569.75 | —× | 0.01× |  |
| switch128 / publicDebug | — | 9209.14 | 3956.39 | —× | 0.43× |  |
| switch128 / compiledParse | — | — | 1431.52 | —× | —× |  |
| switch128 / compiledDebug | — | — | 3052.34 | —× | —× |  |
| wideplain8 / compile | 1080.97 | 1256.39 | 823.29 | 0.76× | 0.66× | 0.75×, 0.77× |
| wideplain8 / parseCore | 18.98 | 26.13 | 19.22 | 1.01× | 0.74× | 1.01×, 1.02× |
| wideplain8 / parseJson | 34.57 | 42.20 | 34.65 | 1.00× | 0.82× | 1.00×, 1.01× |
| wideplain8 / publicParse | 207541.10 | 209448.50 | 998.31 | 0.00× | 0.00× | 0.00×, 0.00× |
| wideplain8 / publicDebug | 1232.59 | 1451.80 | 975.84 | 0.79× | 0.67× | 0.79×, 0.80× |
| wideplain8 / compiledParse | — | — | 135.48 | —× | —× |  |
| wideplain8 / compiledDebug | — | — | 176.19 | —× | —× |  |
| wideif8 / compile | — | 1590.41 | 1182.64 | —× | 0.74× |  |
| wideif8 / parseCore | — | 117.00 | 25.10 | —× | 0.21× |  |
| wideif8 / parseJson | — | 137.04 | 40.46 | —× | 0.30× |  |
| wideif8 / publicParse | — | 217209.70 | 1372.55 | —× | 0.01× |  |
| wideif8 / publicDebug | — | 1860.36 | 1334.11 | —× | 0.72× |  |
| wideif8 / compiledParse | — | — | 146.21 | —× | —× |  |
| wideif8 / compiledDebug | — | — | 184.05 | —× | —× |  |
| wideplain32 / compile | 2531.19 | 2728.56 | 2329.94 | 0.92× | 0.85× | 0.92×, 0.92× |
| wideplain32 / parseCore | 53.33 | 68.11 | 53.95 | 1.01× | 0.79× | 1.01×, 1.01× |
| wideplain32 / parseJson | 84.57 | 102.97 | 84.63 | 1.00× | 0.82× | 1.00×, 1.00× |
| wideplain32 / publicParse | 215689.95 | 218563.55 | 2607.41 | 0.01× | 0.01× | 0.01×, 0.01× |
| wideplain32 / publicDebug | 2861.35 | 3100.56 | 2645.68 | 0.92× | 0.85× | 0.92×, 0.93× |
| wideplain32 / compiledParse | — | — | 188.44 | —× | —× |  |
| wideplain32 / compiledDebug | — | — | 339.26 | —× | —× |  |
| wideif32 / compile | — | 3029.04 | 2815.54 | —× | 0.93× |  |
| wideif32 / parseCore | — | 400.29 | 64.90 | —× | 0.16× |  |
| wideif32 / parseJson | — | 445.13 | 95.53 | —× | 0.21× |  |
| wideif32 / publicParse | — | 226628.70 | 3050.90 | —× | 0.01× |  |
| wideif32 / publicDebug | — | 3721.23 | 3114.67 | —× | 0.84× |  |
| wideif32 / compiledParse | — | — | 214.68 | —× | —× |  |
| wideif32 / compiledDebug | — | — | 369.63 | —× | —× |  |
| wideplain128 / compile | 8243.46 | 8547.81 | 8429.04 | 1.02× | 0.99× | 1.01×, 1.04× |
| wideplain128 / parseCore | 225.77 | 272.43 | 225.30 | 1.00× | 0.83× | 0.99×, 1.01× |
| wideplain128 / parseJson | 329.68 | 370.54 | 319.73 | 0.97× | 0.86× | 0.94×, 1.00× |
| wideplain128 / publicParse | 239866.75 | 245036.40 | 8859.47 | 0.04× | 0.04× | 0.04×, 0.04× |
| wideplain128 / publicDebug | 9338.05 | 9602.68 | 9299.84 | 1.00× | 0.97× | 0.99×, 1.00× |
| wideplain128 / compiledParse | — | — | 450.29 | —× | —× |  |
| wideplain128 / compiledDebug | — | — | 1043.40 | —× | —× |  |
| wideif128 / compile | — | 8820.14 | 9154.94 | —× | 1.04× |  |
| wideif128 / parseCore | — | 2798.11 | 263.22 | —× | 0.09× |  |
| wideif128 / parseJson | — | 2923.39 | 368.10 | —× | 0.13× |  |
| wideif128 / publicParse | — | 262345.45 | 9697.65 | —× | 0.04× |  |
| wideif128 / publicDebug | — | 12672.61 | 10118.78 | —× | 0.80× |  |
| wideif128 / compiledParse | — | — | 493.55 | —× | —× |  |
| wideif128 / compiledDebug | — | — | 1085.48 | —× | —× |  |
| mixedplain128 / compile | 934.38 | 1074.35 | 699.74 | 0.75× | 0.65× | 0.73×, 0.76× |
| mixedplain128 / parseCore | 598.59 | 765.59 | 612.87 | 1.02× | 0.80× | 1.01×, 1.04× |
| mixedplain128 / parseJson | 914.69 | 1084.86 | 923.65 | 1.01× | 0.85× | 1.00×, 1.02× |
| mixedplain128 / publicParse | 220205.65 | 225815.25 | 1842.44 | 0.01× | 0.01× | 0.01×, 0.01× |
| mixedplain128 / publicDebug | 3667.07 | 4184.14 | 3219.36 | 0.88× | 0.77× | 0.86×, 0.90× |
| mixedplain128 / compiledParse | — | — | 1084.25 | —× | —× |  |
| mixedplain128 / compiledDebug | — | — | 2659.69 | —× | —× |  |
| mixedswitch128 / compile | — | 1820.77 | 1503.61 | —× | 0.83× |  |
| mixedswitch128 / parseCore | — | 5553.53 | 900.41 | —× | 0.16× |  |
| mixedswitch128 / parseJson | — | 5988.11 | 1229.63 | —× | 0.21× |  |
| mixedswitch128 / publicParse | — | 248380.70 | 2981.49 | —× | 0.01× |  |
| mixedswitch128 / publicDebug | — | 10706.31 | 4364.29 | —× | 0.41× |  |
| mixedswitch128 / compiledParse | — | — | 1430.55 | —× | —× |  |
| mixedswitch128 / compiledDebug | — | — | 3042.24 | —× | —× |  |
| nestedif128 / compile | — | 1911.01 | 1594.59 | —× | 0.83× |  |
| nestedif128 / parseCore | — | 6702.14 | 969.56 | —× | 0.14× |  |
| nestedif128 / parseJson | — | 7316.05 | 1287.67 | —× | 0.18× |  |
| nestedif128 / publicParse | — | 252960.75 | 3157.59 | —× | 0.01× |  |
| nestedif128 / publicDebug | — | 12373.23 | 4530.22 | —× | 0.37× |  |
| nestedif128 / compiledParse | — | — | 1523.94 | —× | —× |  |
| nestedif128 / compiledDebug | — | — | 3102.63 | —× | —× |  |

### Conditional price

| Pair / operation | Feature conditional/plain | Optimized conditional/plain |
|---|---:|---:|
| if1/plain1 / compile | 1.23× | 1.51× |
| if1/plain1 / parseCore | 2.41× | 1.25× |
| if1/plain1 / parseJson | 1.88× | 1.14× |
| if1/plain1 / compiledParse | — | 1.03× |
| if1/plain1 / compiledDebug | — | 1.04× |
| switch1/plain1 / compile | 1.31× | 1.59× |
| switch1/plain1 / parseCore | 2.47× | 1.25× |
| switch1/plain1 / parseJson | 1.91× | 1.14× |
| switch1/plain1 / compiledParse | — | 1.02× |
| switch1/plain1 / compiledDebug | — | 0.99× |
| if128/plain128 / compile | 1.25× | 1.46× |
| if128/plain128 / parseCore | 5.18× | 1.41× |
| if128/plain128 / parseJson | 4.09× | 1.28× |
| if128/plain128 / compiledParse | — | 1.27× |
| if128/plain128 / compiledDebug | — | 1.14× |
| switch128/plain128 / compile | 1.31× | 1.57× |
| switch128/plain128 / parseCore | 5.26× | 1.41× |
| switch128/plain128 / parseJson | 4.21× | 1.28× |
| switch128/plain128 / compiledParse | — | 1.27× |
| switch128/plain128 / compiledDebug | — | 1.14× |
| wideif8/wideplain8 / compile | 1.27× | 1.44× |
| wideif8/wideplain8 / parseCore | 4.48× | 1.31× |
| wideif8/wideplain8 / parseJson | 3.25× | 1.17× |
| wideif8/wideplain8 / compiledParse | — | 1.08× |
| wideif8/wideplain8 / compiledDebug | — | 1.04× |
| wideif32/wideplain32 / compile | 1.11× | 1.21× |
| wideif32/wideplain32 / parseCore | 5.88× | 1.20× |
| wideif32/wideplain32 / parseJson | 4.32× | 1.13× |
| wideif32/wideplain32 / compiledParse | — | 1.14× |
| wideif32/wideplain32 / compiledDebug | — | 1.09× |
| wideif128/wideplain128 / compile | 1.03× | 1.09× |
| wideif128/wideplain128 / parseCore | 10.27× | 1.17× |
| wideif128/wideplain128 / parseJson | 7.89× | 1.15× |
| wideif128/wideplain128 / compiledParse | — | 1.10× |
| wideif128/wideplain128 / compiledDebug | — | 1.04× |
| mixedswitch128/mixedplain128 / compile | 1.69× | 2.15× |
| mixedswitch128/mixedplain128 / parseCore | 7.25× | 1.47× |
| mixedswitch128/mixedplain128 / parseJson | 5.52× | 1.33× |
| mixedswitch128/mixedplain128 / compiledParse | — | 1.32× |
| mixedswitch128/mixedplain128 / compiledDebug | — | 1.14× |
| nestedif128/mixedplain128 / compile | 1.78× | 2.28× |
| nestedif128/mixedplain128 / parseCore | 8.75× | 1.58× |
| nestedif128/mixedplain128 / parseJson | 6.74× | 1.39× |
| nestedif128/mixedplain128 / compiledParse | — | 1.41× |
| nestedif128/mixedplain128 / compiledDebug | — | 1.17× |

## Native allocated bytes per operation

| Scenario / operation | Main | Feature | Optimized |
|---|---:|---:|---:|
| header/compile | 94224 | 135976 | 36496 |
| header/parse | 1840 | 2128 | 1848 |
| header/debug | 2408 | 2696 | 2456 |
| header/span | 1848 | 2136 | 1856 |
| fields128/compile | 306456 | 354208 | 264728 |
| fields128/parse | 38188 | 42525 | 38120 |
| fields128/debug | 65156 | 69384 | 66222 |
| fields128/span | 38278 | 42472 | 38287 |
| bytes1024/compile | 93664 | 135464 | 34800 |
| bytes1024/parse | 92408 | 125368 | 91904 |
| bytes1024/debug | 256344 | 289304 | 255864 |
| bytes1024/span | 125152 | 158112 | 124648 |
| nested16/compile | 100352 | 142824 | 42656 |
| nested16/parse | 10352 | 14544 | 9848 |
| nested16/debug | 20312 | 28472 | 21760 |
| nested16/span | 10840 | 15032 | 10336 |
| dynamic32/compile | 93976 | 135824 | 36264 |
| dynamic32/parse | 6158 | 7557 | 4624 |
| dynamic32/debug | 14447 | 15842 | 12952 |
| dynamic32/span | 6166 | 7564 | 4632 |
| text64/compile | 95216 | 137064 | 36480 |
| text64/parse | 8352 | 10624 | 7848 |
| text64/debug | 24872 | 27144 | 24400 |
| text64/span | 10376 | 12648 | 9872 |
| plain1/compile | 100304 | 142776 | 42648 |
| plain1/parse | 2792 | 3264 | 2288 |
| plain1/debug | 3424 | 4144 | 3064 |
| plain1/span | 2800 | 3272 | 2296 |
| if1/compile | — | 148592 | 54776 |
| if1/parse | — | 7902 | 2440 |
| if1/debug | — | 8766 | 3216 |
| if1/span | — | 7911 | 2448 |
| switch1/compile | — | 153920 | 60296 |
| switch1/parse | — | 7870 | 2440 |
| switch1/debug | — | 8734 | 3216 |
| switch1/span | — | 7878 | 2448 |
| plain128/compile | 100384 | 142856 | 42728 |
| plain128/parse | 84146 | 120179 | 83641 |
| plain128/debug | 177402 | 245404 | 192512 |
| plain128/span | 88218 | 124250 | 87714 |
| if128/compile | — | 148672 | 54856 |
| if128/parse | — | 712482 | 103096 |
| if128/debug | — | 838321 | 211960 |
| if128/span | — | 716580 | 107172 |
| switch128/compile | — | 154000 | 60376 |
| switch128/parse | — | 708420 | 103096 |
| switch128/debug | — | 834201 | 211960 |
| switch128/span | — | 712447 | 107172 |
| wideplain8/compile | 104728 | 146768 | 47768 |
| wideplain8/parse | 3585 | 4065 | 3593 |
| wideplain8/debug | 6034 | 6514 | 6130 |
| wideplain8/span | 3593 | 4073 | 3601 |
| wideif8/compile | — | 153760 | 68904 |
| wideif8/parse | — | 18611 | 3793 |
| wideif8/debug | — | 21063 | 6329 |
| wideif8/span | — | 18620 | 3801 |
| wideplain32/compile | 145296 | 188488 | 91408 |
| wideplain32/parse | 8788 | 10037 | 8796 |
| wideplain32/debug | 18588 | 19844 | 18884 |
| wideplain32/span | 8796 | 10045 | 8805 |
| wideif32/compile | — | 200280 | 146488 |
| wideif32/parse | — | 58970 | 9189 |
| wideif32/debug | — | 68753 | 19269 |
| wideif32/span | — | 58979 | 9201 |
| wideplain128/compile | 310144 | 357944 | 268544 |
| wideplain128/parse | 39459 | 43907 | 39474 |
| wideplain128/debug | 78935 | 83292 | 79734 |
| wideplain128/span | 39473 | 43853 | 39560 |
| wideif128/compile | — | 388936 | 465816 |
| wideif128/parse | — | 235696 | 40675 |
| wideif128/debug | — | 274633 | 80937 |
| wideif128/span | — | 235631 | 40673 |
| mixedplain128/compile | 100384 | 142856 | 42728 |
| mixedplain128/parse | 84146 | 120178 | 83641 |
| mixedplain128/debug | 177402 | 245404 | 192512 |
| mixedplain128/span | 88219 | 124250 | 87714 |
| mixedswitch128/compile | — | 172096 | 78120 |
| mixedswitch128/parse | — | 1057564 | 105154 |
| mixedswitch128/debug | — | 1183034 | 214036 |
| mixedswitch128/span | — | 1061586 | 109229 |
| nestedif128/compile | — | 164392 | 76360 |
| nestedif128/parse | — | 1327766 | 106179 |
| nestedif128/debug | — | 1453305 | 215064 |
| nestedif128/span | — | 1331830 | 110253 |
