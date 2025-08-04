# GitHub Copilot Instructions for .NET Design Repository

This document provides guidance for GitHub Copilot when working with design proposals in the dotnet/designs repository.

## Repository Structure and Purpose

This repository contains design proposals for the .NET platform, focusing on:
- Runtime designs
- Framework designs  
- SDK designs

Related language-specific design repos:
- C# Language: `dotnet/csharplang`
- VB Language: `dotnet/vblang`
- F# Language: `fsharp/fslang-design`

## Design Document Structure

### File Organization
- **Accepted designs**: `accepted/YYYY/` folders organized by year
- **Proposed designs**: `proposed/` folder for new proposals under review
- **Meta documentation**: `meta/` folder containing templates and guidelines

### Required Files
- Use the template at `meta/template.md` as the foundation
- Follow the guidelines in `meta/proposals.md`
- See `INDEX.md` for a complete listing of existing designs

## Design Document Template

All design docs should follow this structure:

```markdown
# Your Feature

**Owner** [Name](https://github.com/username) | [Name](https://github.com/username)

## Scenarios and User Experience

## Requirements

### Goals

### Non-Goals

## Stakeholders and Reviewers

## Design

## Q & A
```

### Section Guidelines

1. **Title & Owner**: 
   - Clear, descriptive feature name
   - Link to GitHub profiles for primary contacts
   - Bold the role (PM/Dev) if applicable

2. **Opening Summary**:
   - First paragraph should be a concise summary of the problem and proposed solution
   - Write this last for best results
   - Make readers curious and engaged

3. **Scenarios and User Experience**:
   - Start with "happy path" scenarios
   - Progress from simple to advanced use cases
   - Include sample code for APIs or command-line examples
   - Cover end-to-end customer scenarios
   - Use mock-ups for UI features

4. **Requirements**:
   - **Goals**: Functional and non-functional requirements for correctness
   - **Non-Goals**: Explicitly scope out related problems
   - Keep high-level, avoid detailed staging (MVP, etc.)

5. **Stakeholders and Reviewers**:
   - List teams and individuals who should be involved
   - Ensure they are tagged/invited early in the process

6. **Design**:
   - Structure as needed by engineering team
   - Include API surfaces, command syntax, UI flows
   - Link to external documents when text format isn't viable
   - Provide enough detail for implementation

7. **Q & A**:
   - Document decisions and rationale
   - Update as questions arise during reviews
   - Link to answers instead of re-explaining

## Best Practices

### Writing Approach
- **Optimize for the reader**: Template helps readers be efficient across multiple proposals
- **Structured thinking**: Writing forces thorough consideration of the problem
- **Enable feedback**: Share early for customer and stakeholder input before coding begins
- **Progressive complexity**: Easy scenarios first, advanced scenarios later

### Content Strategy
- Include 3-5 lines of context before/after when editing
- Provide examples from existing accepted designs
- Reference related work in other proposals
- Consider cross-platform implications
- Address security and performance concerns

### Review Process
- Use proposals for early feedback before implementation
- Tag relevant stakeholders on GitHub
- Update Q&A section based on review feedback
- Move from `proposed/` to `accepted/YYYY/` when approved

## Existing Examples

Reference these well-structured designs:
- [Windows Compatibility Pack](accepted/2018/compat-pack/compat-pack.md)
- [Package Information in API Reference](accepted/2018/package-doc/package-doc.md)
- [Target Framework Names in .NET 5](accepted/2020/net5/net5.md)
- [Single-file Publish](accepted/2020/single-file/design.md)

## Common Patterns

### API Designs
- Include assembly names, type names, method signatures
- Show usage examples
- Consider backwards compatibility
- Address platform-specific considerations

### CLI Tool Designs  
- List all commands and options
- Show command syntax examples
- Consider cross-platform shell differences
- Address installation and discovery

### UI/UX Designs
- Include screen mockups and flow diagrams
- Consider accessibility requirements
- Show progressive disclosure patterns

## Integration Points

### With .NET Ecosystem
- Consider impact on runtime, BCL, SDK, and tooling
- Address workload and targeting pack implications
- Consider single-file and AOT scenarios
- Plan for diagnostics and debugging support

### With External Tools
- Consider impact on IDEs (VS, VS Code, Rider)
- Address package manager integration (NuGet)
- Consider cloud and container scenarios
- Plan for CI/CD pipeline integration

## Review and Approval

- Proposals should be comprehensive but focused
- Include implementation feasibility assessment  
- Consider breaking changes and migration paths
- Address testing strategy and success criteria
- Plan for documentation and sample updates

Remember: The goal is to make readers curious and provide enough context for proper critique before implementation begins.
