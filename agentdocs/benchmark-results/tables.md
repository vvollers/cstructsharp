| Runtime | Scenario | Operation | Main µs | Branch µs | Change | Main → branch bytes/op |
|---|---|---|---:|---:|---:|---:|
| native | header | parse | 0.40 | 0.45 | +12.9% | 1,840 → 2,128 |
| native | header | debug | 0.57 | 0.64 | +12.6% | 2,408 → 2,696 |
| native | header | span | 0.45 | 0.51 | +14.1% | 1,848 → 2,136 |
| native | fields128 | compile | 404.75 | 422.53 | +4.4% | 306,456 → 354,208 |
| native | fields128 | parse | 31.15 | 33.07 | +6.2% | 38,190 → 42,535 |
| native | fields128 | debug | 35.27 | 38.03 | +7.8% | 65,138 → 69,360 |
| native | fields128 | span | 31.88 | 33.57 | +5.3% | 38,279 → 42,469 |
| native | bytes1024 | compile | 40.47 | 50.03 | +23.6% | 93,664 → 135,464 |
| native | bytes1024 | parse | 26.74 | 32.28 | +20.7% | 92,408 → 125,368 |
| native | bytes1024 | debug | 44.84 | 51.04 | +13.8% | 256,344 → 289,304 |
| native | bytes1024 | span | 31.17 | 36.68 | +17.7% | 125,152 → 158,112 |
| native | nested16 | compile | 53.24 | 62.58 | +17.6% | 100,352 → 142,824 |
| native | nested16 | parse | 3.22 | 3.79 | +17.9% | 10,352 → 14,544 |
| native | nested16 | debug | 4.39 | 6.68 | +52.3% | 20,312 → 28,472 |
| native | nested16 | span | 3.47 | 4.08 | +17.5% | 10,840 → 15,032 |
| native | dynamic32 | compile | 42.22 | 51.88 | +22.9% | 93,976 → 135,824 |
| native | dynamic32 | parse | 2.18 | 2.51 | +15.0% | 6,159 → 7,556 |
| native | dynamic32 | debug | 3.13 | 3.43 | +9.4% | 14,448 → 15,842 |
| native | dynamic32 | span | 2.34 | 2.60 | +11.3% | 6,166 → 7,565 |
| native | text64 | compile | 43.45 | 50.46 | +16.1% | 95,216 → 137,064 |
| native | text64 | parse | 2.42 | 2.70 | +11.7% | 8,352 → 10,624 |
| native | text64 | debug | 3.73 | 4.01 | +7.7% | 24,872 → 27,144 |
| native | text64 | span | 2.71 | 2.99 | +10.2% | 10,376 → 12,648 |
| native | plain1 | compile | 52.65 | 59.50 | +13.0% | 100,304 → 142,776 |
| native | plain1 | parse | 0.65 | 0.71 | +9.2% | 2,792 → 3,264 |
| native | plain1 | debug | 0.79 | 0.96 | +21.7% | 3,424 → 4,144 |
| native | plain1 | span | 0.67 | 0.77 | +14.7% | 2,800 → 3,272 |
| native | plain128 | compile | 52.59 | 63.20 | +20.2% | 100,384 → 142,856 |
| native | plain128 | parse | 30.53 | 36.74 | +20.3% | 84,146 → 120,179 |
| native | plain128 | debug | 42.12 | 62.36 | +48.0% | 177,402 → 245,404 |
| native | plain128 | span | 32.73 | 39.32 | +20.1% | 88,218 → 124,250 |
| native | header | compile | 41.75 | 49.84 | +19.4% | 94,224 → 135,976 |
| js | header | compile | 752.94 | 855.77 | +13.7% | — |
| js | header | parseCore | 10.61 | 13.12 | +23.6% | — |
| js | header | parseJson | 20.90 | 24.01 | +14.9% | — |
| js | header | publicParse | 210332.65 | 228531.10 | +8.7% | — |
| js | header | publicDebug | 874.82 | 1056.10 | +20.7% | — |
| js | fields128 | compile | 8366.88 | 9230.25 | +10.3% | — |
| js | fields128 | parseCore | 228.14 | 284.43 | +24.7% | — |
| js | fields128 | parseJson | 323.17 | 382.55 | +18.4% | — |
| js | fields128 | publicParse | 243364.40 | 263808.30 | +8.4% | — |
| js | fields128 | publicDebug | 9477.68 | 10317.06 | +8.9% | — |
| js | bytes1024 | compile | 715.73 | 883.42 | +23.4% | — |
| js | bytes1024 | parseCore | 491.63 | 635.38 | +29.2% | — |
| js | bytes1024 | parseJson | 744.24 | 906.86 | +21.8% | — |
| js | bytes1024 | publicParse | 218595.50 | 237375.05 | +8.6% | — |
| js | bytes1024 | publicDebug | 4688.16 | 5172.01 | +10.3% | — |
| js | nested16 | compile | 984.52 | 1164.21 | +18.3% | — |
| js | nested16 | parseCore | 70.24 | 93.99 | +33.8% | — |
| js | nested16 | parseJson | 114.48 | 142.38 | +24.4% | — |
| js | nested16 | publicParse | 212912.65 | 231125.85 | +8.6% | — |
| js | nested16 | publicDebug | 1333.58 | 1608.67 | +20.6% | — |
| js | dynamic32 | compile | 728.88 | 885.53 | +21.5% | — |
| js | dynamic32 | parseCore | 40.21 | 49.40 | +22.8% | — |
| js | dynamic32 | parseJson | 61.27 | 72.40 | +18.2% | — |
| js | dynamic32 | publicParse | 208925.75 | 225538.30 | +8.0% | — |
| js | dynamic32 | publicDebug | 1033.88 | 1223.05 | +18.3% | — |
| js | text64 | compile | 770.71 | 933.03 | +21.1% | — |
| js | text64 | parseCore | 52.69 | 65.45 | +24.2% | — |
| js | text64 | parseJson | 63.21 | 76.70 | +21.3% | — |
| js | text64 | publicParse | 224479.10 | 226218.25 | +0.8% | — |
| js | text64 | publicDebug | 1188.44 | 1325.64 | +11.5% | — |
| js | plain1 | compile | 1029.32 | 1161.15 | +12.8% | — |
| js | plain1 | parseCore | 18.31 | 22.11 | +20.8% | — |
| js | plain1 | parseJson | 33.35 | 37.32 | +11.9% | — |
| js | plain1 | publicParse | 225691.15 | 227653.50 | +0.9% | — |
| js | plain1 | publicDebug | 1157.05 | 1317.53 | +13.9% | — |
| js | plain128 | compile | 1024.53 | 1144.31 | +11.7% | — |
| js | plain128 | parseCore | 656.35 | 808.07 | +23.1% | — |
| js | plain128 | parseJson | 975.52 | 1156.01 | +18.5% | — |
| js | plain128 | publicParse | 235442.30 | 225888.05 | -4.1% | — |
| js | plain128 | publicDebug | 3723.52 | 4590.21 | +23.3% | — |

| Runtime | Records | Operation | Plain µs | If µs (ratio) | Switch µs (ratio) |
|---|---:|---|---:|---:|---:|
| native | 1 | compile | 59.50 | 77.47 (1.30×) | 81.49 (1.37×) |
| native | 1 | parse | 0.71 | 2.31 (3.27×) | 2.41 (3.41×) |
| native | 1 | debug | 0.96 | 2.67 (2.78×) | 2.57 (2.68×) |
| native | 1 | span | 0.77 | 2.36 (3.08×) | 2.48 (3.23×) |
| native | 128 | compile | 63.20 | 77.52 (1.23×) | 82.20 (1.30×) |
| native | 128 | parse | 36.74 | 248.28 (6.76×) | 239.83 (6.53×) |
| native | 128 | debug | 62.36 | 279.70 (4.49×) | 279.91 (4.49×) |
| native | 128 | span | 39.32 | 245.08 (6.23×) | 245.37 (6.24×) |
| js | 1 | compile | 1161.15 | 1476.33 (1.27×) | 1565.88 (1.35×) |
| js | 1 | parseCore | 22.11 | 54.85 (2.48×) | 54.84 (2.48×) |
| js | 1 | parseJson | 37.32 | 71.12 (1.91×) | 68.46 (1.83×) |
| js | 1 | publicParse | 227653.50 | 230967.20 (1.01×) | 218426.45 (0.96×) |
| js | 1 | publicDebug | 1317.53 | 1659.13 (1.26×) | 1695.88 (1.29×) |
| js | 128 | compile | 1144.31 | 1431.16 (1.25×) | 1495.02 (1.31×) |
| js | 128 | parseCore | 808.07 | 4303.48 (5.33×) | 4302.76 (5.32×) |
| js | 128 | parseJson | 1156.01 | 4822.42 (4.17×) | 4767.70 (4.12×) |
| js | 128 | publicParse | 225888.05 | 245537.25 (1.09×) | 243546.75 (1.08×) |
| js | 128 | publicDebug | 4590.21 | 8737.20 (1.90×) | 9308.84 (2.03×) |
