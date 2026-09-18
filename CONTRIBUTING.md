# Contributing

Thanks for helping. Issues and pull requests are welcome.

## Before you open a pull request

1. Run `build.cmd` and `test.cmd`. Both must succeed; warnings count as errors.
2. Keep the code in C# 5 (the language version of the compiler that ships with Windows), with no third-party packages. That is what lets anyone build it without installing anything.
3. Logic that needs no UI goes in `src/Core.cs`, with a test in `tests/SelfTest.cs`.
4. Anything that touches the API key, the network, or the files the widget writes needs a note in the pull request on how it keeps to [SECURITY.md](SECURITY.md).
5. Add a line under "Unreleased" in [CHANGELOG.md](CHANGELOG.md).

If you change the widget's appearance, include a screenshot in the pull request.

## Reporting bugs

Open an issue with your Windows version, display scaling, what the widget shows, and the contents of `status.txt`. It never contains your key; you can replace the dollar amounts with `x`. Security problems go through [SECURITY.md](SECURITY.md) instead.

By contributing, you agree your contribution is licensed under the [MIT License](LICENSE) and that you follow the [Code of Conduct](CODE_OF_CONDUCT.md).
