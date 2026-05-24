<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the most recent
feature plan at specs/010-github-actions-cicd/plan.md

010-github-actions-cicd replaces the Azure DevOps pipeline definitions
in deploy/ with two GitHub Actions workflows: client.yml (Blazor WASM →
Azure Static Web App) and server.yml (Docker → ACR → Azure Container
App). Both workflows trigger on push to main, use a production GitHub
Environment, and store all credentials in GitHub Secrets. The server
workflow tags images with both the commit SHA and latest. This feature
also requires amending Constitution Principle V (Azure DevOps →
GitHub Actions). See specs/010-github-actions-cicd/ for plan, research
(action versions, ACR auth, Container App update mechanism), data-model
(workflow config and secrets table), and a seven-test manual quickstart.

Prior feature (in progress): 009-transit-soundscape — frontend-only
emergent musical soundscape from live MARTA bus movement (Tone.js,
route=instrument, vehicle=pitch, C-minor pentatonic, procedural
trigger points at 200m spacing). No server changes.
<!-- SPECKIT END -->
