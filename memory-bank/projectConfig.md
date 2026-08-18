# Project Configuration

## ALA Memory Bank

- **Memory Bank File Contract Version**: 2.0.0-codex.1
- **Memory Bank Plugin Package**: ala 2.2.1-codex.1
- **Initialized**: 2026-08-18
- **Last Updated**: 2026-08-18

## Git & Branching

```yaml
metadata_branch: main
protected_branches: [main]
pr_target: main
sync_automation: none
archive_strategy: push-and-pr
worktree_root: ../ala-worktrees/racehunter/
```

## Team

```yaml
team:
  alexander1113@hotmail.com: Alex
```

## UAT

- **default_sections**: happy,mobile
- **default_skip_ux_check**: false
- **default_environment**: dev
- **artifact_git_policy**: ignore
- **uat_required_for_archive**: false

## Privacy

- **usage_telemetry**: disabled-and-not-implemented
- **automatic_session_logging**: disabled-and-not-implemented
