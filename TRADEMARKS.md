# Trademarks

**This project is not affiliated with, endorsed by, or sponsored by Mermaid
Chart Inc., the Mermaid open-source project, or The Walt Disney Company.**

## The position, stated plainly

Merpeeps reads and writes files written in the Mermaid diagram syntax. There is no
way to describe what it does without naming that syntax, so the word "Mermaid"
appears in the documentation, in package descriptions and in keywords. That use
is **descriptive** — it identifies the file format the tools work with, in the
same way a tool can say it reads PDF or edits Markdown.

Licence and trademark are separate questions, and it is worth being explicit
about both:

- The Mermaid project is distributed under the **MIT licence**, which grants
  copyright permissions only. It contains no trademark clause and conveys no
  trademark rights. Nothing here depends on a grant of trademark rights.
- `MERMAID CHART` is a **registered trademark** of Mermaid Chart Inc. (USPTO
  serial 98035110, Class 42, registered 30 December 2025; serial 98035108,
  Class 9, pending). All trademarks are the property of their respective owners.

## On the name "Merpeeps"

"Merpeeps" is a coined word: *mer-* (the sea, from Latin *mare*) plus *peeps*,
colloquial English for people. It is a playful rendering of "merfolk" — the
collective noun for the inhabitants of the sea in folklore.

It is not a character, not a brand belonging to anyone else, and not derived from
any fictional work. Names such as Ariel, Sebastian, Ursula, Flounder and King
Triton are associated with The Walt Disney Company's *The Little Mermaid* and
were deliberately avoided.

It was chosen the way ecosystem tools commonly are: a related but distinct word
from the same family, signalling kinship without claiming identity. The Python
ecosystem's `Anaconda` follows the same pattern — a different snake, not a claim
to be Python.

The name was checked against npm, crates.io, NuGet and GitHub before adoption.
Nothing in the diagram space uses it. That check matters more than registry
availability alone: a name can be free to publish under and still be
indistinguishable, to a user searching, from something that already exists.

## What this project deliberately does not do

| | |
|---|---|
| Package names | `merpeeps-mermaid-layout`, `Merpeeps.Core` — "mermaid" appears only as a descriptor of the format, never as the distinguishing element |
| Product name | Merpeeps — a common English word, not "Mermaid" and not a character |
| Logo | Original. No third-party logo is used or reproduced |
| Claims of affiliation | None |
| Suggestion of official status | None. Official Mermaid packages live in the `@mermaid-js` scope; ours are unscoped and named for this project |

The word "Mermaid" is used as a noun for the file format ("a Mermaid diagram"),
never as an adjective on our own products ("Mermaid Editor").

## Why "mermaid" appears in the npm package name

npm search matches on the package name far more strongly than on keywords. A
search for "mermaid" returns essentially only packages with "mermaid" in the
name — measured, not assumed — so a package without it cannot be found by the
people it exists for.

This follows established community practice: `rehype-mermaid`,
`vitepress-plugin-mermaid`, `mermaid-isomorphic`, `mdx-mermaid`,
`codemirror-lang-mermaid` and many others have used the same construction for
years. In each the mark is used **descriptively**, to name the format the
package handles.

The distinguishing element of our name is `merpeeps`. The package does not claim
to be Mermaid, to be official, or to be an enhanced version of it.

## If you think this is wrong

If you represent Mermaid Chart Inc. or the Mermaid project and consider any use
of the name here inappropriate, please open an issue. The intent is descriptive
accuracy and interoperability, not association — and a name is cheap to change,
which is exactly why none of the value here is invested in one.

## Your use of this software

Nothing in this file restricts your use of Merpeeps itself, which is MIT licensed.
This concerns names and logos only, which no software licence covers.
