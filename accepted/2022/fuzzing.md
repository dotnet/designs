# Out-of-box fuzzing 

**Owner** [Samuele Resca](https://github.com/samueleresca) 

Fuzzing is a testing technique that manipulates programs inputs to find bugs and crashes in an implementation.
While fuzz testing is possible by using open-source libraries (see https://github.com/Metalnem/sharpfuzz), there is no out-of-the-box tooling or support. Moreover, there isn't a unified way of fuzzing.
This proposal is for including the fuzz testing capabilities as an out-of-box feature in the .NET toolchain.
Having an out-of-box security/reliability testing practice like this would be beneficial:
- Providing a unified way of fuzzing in the .NET ecosystem
- Promoting reliability and security testing practices in the .NET ecosystem (See the list of trophies found by [sharpfuzz](https://github.com/Metalnem/sharpfuzz#trophies)


### Fuzzing in other toolchains

- A similar approach has been taken by Golang (see: [Draft proposal fuzzing](https://go.googlesource.com/proposal/+/master/design/draft-fuzzing.md), [golang/go: add fuzz test support](https://github.com/golang/go/issues/44551)
- LibFuzzer is part of LLVM https://llvm.org/docs/LibFuzzer.html


## Scenarios and User Experience

### Implementing and running a fuzzer session

Berk wants to run a fuzz test on it's own library. So it implements a new fuzz test that provide a string input to his own custom parser:

```csharp
    using FuzzTarget;
    using MyCustomDataParser;

    [FuzzTarget]
    public void ParserFuzz(string input)
    {
        var data = MyCustomDataParser.Parse(input);
    }
```

Berk proceeds by instrumenting the code and starting the fuzzing process using the following command:

```
dotnet fuzz --target ParserFuzz
```

### Running a fuzz test using a custom input seed

Berk wants to run a seeded fuzz test by specifying a custom input. Berk proceeds by creating a new `input.txt` file with the following content:

```
"///cotn3nt汉///"
```

Berk proceeds by instrumenting the code and starting the fuzzing process using the following command:

```
dotnet fuzz --target ParserFuzz --input input.txt
```

The fuzzer will start the fuzzing session by mutating the input in the `input.txt` file.

### The fuzz session crashes during a session

Berk spots a crash output in the execution of the fuzzing session:

```
#2	INITED cov: 2 ft: 2 corp: 1/1b exec/s: 0
#402	NEW    cov: 3 ft: 3 corp: 2/5b exec/s: 0
#415	REDUCE cov: 3 ft: 3 corp: 2/4b exec/s: 0
#426	REDUCE cov: 3 ft: 3 corp: 2/3b exec/s: 0
#437	REDUCE cov: 4 ft: 4 corp: 3/4b exec/s: 0 
#9460	NEW    cov: 5 ft: 5 corp: 4/6b exec/s: 0
#9463	NEW    cov: 6 ft: 6 corp: 5/9b exec/s: 0
ERROR: .NET fuzzer crashed
<stacktrace>
Test unit written to ./crash-<HASH>
```
_(output taken from a LibFuzzer execution)_

Berk can reproduce the crash by running the following command:

```csharp
dotnet fuzz --target ParserFuzz --input ./crash-<HASH>
```

### The fuzz session run with a timeout

Berk wants to run the fuzz testing process in CI and run it on a weekly schedule for a specific timeout (in sec) or until it the session find a crash:

```yaml
name: dotnet package
on:
  schedule:
    - cron:  '0 0 * * 0'
jobs:
  build:
    steps:
      ...
      - name: FuzzTarget1
        run: dotnet fuzz --target ParserFuzz --timeout 21600
```

## Requirements

### Goals

- Having an unified way of fuzz testing in .NET on every platform (Windows, Linux and MacOS).
- Must provide a way to perform coverage-guided fuzzing by instrumenting the target code.
- Must provide a way to implement a fuzz test by marking a test method with an attribute.
- Must provide out-of-box support for built-in value types and string type.
- The fuzzing command must provide a way to start the fuzzing process based on an input (seed corpus).
- The fuzzing command must provide an options for continuing fuzzing after a crash.
- The fuzzing command provide an option for specifying a running timeout.
- The fuzzing command provide an option for specifying a number of running threads.
- The fuzzing process must output a crash report (including the input that crashed and the exception) if a crash is detected.
- The fuzzing process must output a coverage report displaying the progress of the running fuzzer.


### Non-Goals
\- 
## Stakeholders and Reviewers

- [Nemanja Mijailovic](https://github.com/Metalnem) Author of [sharpfuzz](https://github.com/Metalnem/sharpfuzz) (Reviewer)

## Design

### Target implementation

The fuzz target is the code instrumented to run fuzz test. Below an example of fuzz test implementation, similar to an existing example in the [sharpfuzz](https://github.com/Metalnem/sharpfuzz) docs:

```csharp
    using FuzzTarget;
    using MyCustomDataParser;

    [FuzzTarget]
    public void ParserFuzz(string input)
    {
        var data = MyCustomDataParser.Parse(input);
    }
```

### Command

Below some examples of commands to run the fuzz target implemented above:
```
  dotnet fuzz --target ParserFuzz
```


### Output

Below a possible example of output (similar to a libFuzzer output):

```
#2	INITED cov: 2 ft: 2 corp: 1/1b exec/s: 0
#402	NEW    cov: 3 ft: 3 corp: 2/5b exec/s: 0
#415	REDUCE cov: 3 ft: 3 corp: 2/4b exec/s: 0
#426	REDUCE cov: 3 ft: 3 corp: 2/3b exec/s: 0
#437	REDUCE cov: 4 ft: 4 corp: 3/4b exec/s: 0 
#9460	NEW    cov: 5 ft: 5 corp: 4/6b exec/s: 0
#9463	NEW    cov: 6 ft: 6 corp: 5/9b exec/s: 0
ERROR: .NET fuzzer crashed
<stacktrace>
Test unit written to ./crash-<HASH>

```

## Q & A


### Is the feature running on libFuzzer and/or AFL or should implement a .NET fuzzer?

[...]

