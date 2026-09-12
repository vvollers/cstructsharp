| Runtime | Change | Scenario | Operation | Control µs (start–end) | Changed µs | Change vs control midpoint | Bytes/op, control → changed |
|---|---|---|---|---:|---:|---:|---:|
| native | registry | header | compile | 50.98–46.53 | 42.95 | -11.9% | 135,976 → 94,888 |
| native | registry | header | parse | 0.52–0.44 | 0.48 | -0.6% | 2,128 → 2,128 |
| native | registry | fields128 | compile | 419.55–378.37 | 434.70 | +9.0% | 354,208 → 313,120 |
| native | registry | fields128 | parse | 33.90–31.61 | 32.76 | +0.0% | 42,518 → 42,517 |
| native | registry | bytes1024 | compile | 50.18–44.80 | 42.36 | -10.8% | 135,464 → 94,376 |
| native | registry | bytes1024 | parse | 32.37–28.59 | 30.84 | +1.2% | 125,368 → 125,368 |
| native | registry | nested16 | compile | 62.65–56.13 | 55.65 | -6.3% | 142,824 → 101,728 |
| native | registry | nested16 | parse | 3.90–3.48 | 3.82 | +3.5% | 14,544 → 14,544 |
| native | registry | plain128 | compile | 62.66–55.75 | 55.67 | -6.0% | 142,856 → 101,760 |
| native | registry | plain128 | parse | 37.24–33.59 | 36.34 | +2.6% | 120,179 → 120,179 |
| native | bookkeeping | header | compile | 50.98–46.53 | 50.17 | +2.9% | 135,976 → 135,944 |
| native | bookkeeping | header | parse | 0.52–0.44 | 0.45 | -5.9% | 2,128 → 1,944 |
| native | bookkeeping | fields128 | compile | 419.55–378.37 | 417.49 | +4.6% | 354,208 → 354,176 |
| native | bookkeeping | fields128 | parse | 33.90–31.61 | 31.80 | -2.9% | 42,518 → 42,435 |
| native | bookkeeping | bytes1024 | compile | 50.18–44.80 | 50.59 | +6.5% | 135,464 → 135,432 |
| native | bookkeeping | bytes1024 | parse | 32.37–28.59 | 31.78 | +4.3% | 125,368 → 125,184 |
| native | bookkeeping | nested16 | compile | 62.65–56.13 | 63.61 | +7.1% | 142,824 → 142,800 |
| native | bookkeeping | nested16 | parse | 3.90–3.48 | 3.48 | -5.8% | 14,544 → 11,416 |
| native | bookkeeping | plain128 | compile | 62.66–55.75 | 63.69 | +7.6% | 142,856 → 142,832 |
| native | bookkeeping | plain128 | parse | 37.24–33.59 | 33.73 | -4.8% | 120,179 → 96,443 |
| native | fixedpoint | header | compile | 50.98–46.53 | 51.00 | +4.6% | 135,976 → 135,976 |
| native | fixedpoint | header | parse | 0.52–0.44 | 0.47 | -2.2% | 2,128 → 2,032 |
| native | fixedpoint | fields128 | compile | 419.55–378.37 | 421.40 | +5.6% | 354,208 → 354,208 |
| native | fixedpoint | fields128 | parse | 33.90–31.61 | 32.69 | -0.2% | 42,518 → 38,373 |
| native | fixedpoint | bytes1024 | compile | 50.18–44.80 | 51.04 | +7.5% | 135,464 → 135,464 |
| native | fixedpoint | bytes1024 | parse | 32.37–28.59 | 29.25 | -4.0% | 125,368 → 92,600 |
| native | fixedpoint | nested16 | compile | 62.65–56.13 | 63.93 | +7.6% | 142,824 → 142,824 |
| native | fixedpoint | nested16 | parse | 3.90–3.48 | 3.78 | +2.5% | 14,544 → 13,488 |
| native | fixedpoint | plain128 | compile | 62.66–55.75 | 63.30 | +6.9% | 142,856 → 142,856 |
| native | fixedpoint | plain128 | parse | 37.24–33.59 | 35.80 | +1.1% | 120,179 → 107,890 |
| js | registry | header | compile | 844.77–852.26 | 760.05 | -10.4% | — |
| js | registry | header | parseCore | 13.23–11.56 | 12.95 | +4.4% | — |
| js | registry | fields128 | compile | 9101.15–8203.00 | 8946.41 | +3.4% | — |
| js | registry | fields128 | parseCore | 282.22–256.78 | 281.11 | +4.3% | — |
| js | registry | bytes1024 | compile | 872.13–789.79 | 772.72 | -7.0% | — |
| js | registry | bytes1024 | parseCore | 643.81–558.57 | 632.14 | +5.1% | — |
| js | registry | nested16 | compile | 1188.63–1194.49 | 1067.04 | -10.5% | — |
| js | registry | nested16 | parseCore | 95.05–94.25 | 93.70 | -1.0% | — |
| js | registry | plain128 | compile | 1172.19–1159.86 | 1049.62 | -10.0% | — |
| js | registry | plain128 | parseCore | 817.67–809.09 | 807.29 | -0.7% | — |
| js | bookkeeping | header | compile | 844.77–852.26 | 845.36 | -0.4% | — |
| js | bookkeeping | header | parseCore | 13.23–11.56 | 11.56 | -6.8% | — |
| js | bookkeeping | fields128 | compile | 9101.15–8203.00 | 9134.18 | +5.6% | — |
| js | bookkeeping | fields128 | parseCore | 282.22–256.78 | 257.87 | -4.3% | — |
| js | bookkeeping | bytes1024 | compile | 872.13–789.79 | 860.11 | +3.5% | — |
| js | bookkeeping | bytes1024 | parseCore | 643.81–558.57 | 608.39 | +1.2% | — |
| js | bookkeeping | nested16 | compile | 1188.63–1194.49 | 1186.48 | -0.4% | — |
| js | bookkeeping | nested16 | parseCore | 95.05–94.25 | 82.83 | -12.5% | — |
| js | bookkeeping | plain128 | compile | 1172.19–1159.86 | 1172.09 | +0.5% | — |
| js | bookkeeping | plain128 | parseCore | 817.67–809.09 | 717.59 | -11.8% | — |
| js | fixedpoint | header | compile | 844.77–852.26 | 867.29 | +2.2% | — |
| js | fixedpoint | header | parseCore | 13.23–11.56 | 12.83 | +3.5% | — |
| js | fixedpoint | fields128 | compile | 9101.15–8203.00 | 9098.79 | +5.2% | — |
| js | fixedpoint | fields128 | parseCore | 282.22–256.78 | 273.36 | +1.4% | — |
| js | fixedpoint | bytes1024 | compile | 872.13–789.79 | 885.62 | +6.6% | — |
| js | fixedpoint | bytes1024 | parseCore | 643.81–558.57 | 548.21 | -8.8% | — |
| js | fixedpoint | nested16 | compile | 1188.63–1194.49 | 1213.23 | +1.8% | — |
| js | fixedpoint | nested16 | parseCore | 95.05–94.25 | 91.35 | -3.5% | — |
| js | fixedpoint | plain128 | compile | 1172.19–1159.86 | 1189.02 | +2.0% | — |
| js | fixedpoint | plain128 | parseCore | 817.67–809.09 | 711.61 | -12.5% | — |

| Runtime | Fields | Plain µs | Conditional µs | Ratio | Plain → conditional bytes/op |
|---|---:|---:|---:|---:|---:|
| native | 8 | 1.02 | 5.25 | 5.13× | 4,065 → 19,188 |
| native | 32 | 4.25 | 24.29 | 5.71× | 10,036 → 61,074 |
| native | 128 | 31.18 | 208.90 | 6.70× | 43,890 → 244,307 |
| js | 8 | 22.53 | 101.25 | 4.49× | — |
| js | 32 | 62.39 | 427.13 | 6.85× | — |
| js | 128 | 283.22 | 3054.33 | 10.78× | — |
