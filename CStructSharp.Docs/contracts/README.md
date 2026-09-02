# Tracked documentation contracts

This directory stores the machine-readable descriptions used by CStructSharp documentation validators and tests.
They cover the layout language, managed and browser compatibility, quality limits, performance expectations, and
release checks.

Human-readable pages link here when readers or maintainers need the exact data. Generated reports do not belong in
this directory.

When you change one of these files, update the validator or test that reads it and any page that describes the same
behavior. Keeping those changes together makes it possible to review both the rule and its explanation.
