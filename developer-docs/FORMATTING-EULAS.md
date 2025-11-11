# Formatting EULAs Consistently

We keep text licences (e.g., `EULA-codex.txt`) aligned with the house style used in `../SslDecoder/EULA.txt`. When editing another product’s EULA, follow this checklist:

1. **Copy the structure**  
   - Title block with `END USER LICENSE AGREEMENT (EULA)`  
   - Version + effective date lines, indented with four spaces  
   - Product tag line (`FOR <Product>`)  
   - Intro paragraphs (all-caps warning)

2. **Definitions section**  
   - Use a markdown-style table: “Term”/“Definition” headers with a dashed separator line  
   - Keep long definitions wrapped with `|` on continuation lines (see SslDecoder example)  
   - Preserve the original wording exactly—only adjust whitespace/newlines

3. **Numbered headings**  
   - Each major clause (`2. Licence Grant`, `3. Subscription and Termination`, etc.) should have a blank line before and after  
   - Subclauses (e.g. `2.1.`) are bolded with two spaces between the number and the text, mirroring the reference file

4. **Paragraph wrapping**  
   - Use 70–72 character width for readability in terminals  
   - Insert blank lines between paragraphs and around lists/quotes  
   - Never change legal text—only whitespace

5. **Verification**  
   - Compare against `../SslDecoder/EULA.txt` to ensure spacing, indentation, and table layout match  
   - Avoid tabs; use four spaces for the version/effective-date block

This doc serves as the template whenever we need to reflow another EULA without altering content.
