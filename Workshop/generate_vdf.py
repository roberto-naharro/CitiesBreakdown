#!/usr/bin/env python3
"""Generate /tmp/item.vdf for steamcmd workshop_build_item.

Reads GITHUB_WORKSPACE, CHANGE_NOTE, and WORKSHOP_ITEM_ID from the environment.
Used by .github/workflows/workshop-deploy.yml.
"""
import os

workspace = os.environ['GITHUB_WORKSPACE']
note = os.environ.get('CHANGE_NOTE', 'Update')
item_id = os.environ.get('WORKSHOP_ITEM_ID', '')
if not item_id:
    raise SystemExit("ERROR: WORKSHOP_ITEM_ID secret is not set. Do the first publish manually with ./publish.sh, then add the ID as a GitHub secret.")

desc_path = os.path.join(workspace, 'Workshop', 'description.txt')


def vdf_escape(s):
    """Escape a string for use as a VDF single-line value."""
    return s.replace('\\', '\\\\').replace('"', '\\"').replace('\r', '').replace('\n', ' ')


vdf = (
    '"workshopitem"\n'
    '{\n'
    '\t"appid"\t\t\t"255710"\n'
    '\t"publishedfileid"\t"' + item_id + '"\n'
    '\t"contentfolder"\t\t"' + workspace + '/dist/BreakdownRevisited"\n'
    '\t"previewfile"\t\t"' + workspace + '/Workshop/PreviewImage.png"\n'
    '\t"descriptionfile"\t"' + desc_path + '"\n'
    '\t"changenote"\t\t"' + vdf_escape(note)[:7900] + '"\n'
    '}\n'
)

with open('/tmp/item.vdf', 'w') as f:
    f.write(vdf)

print("Generated /tmp/item.vdf:")
print(vdf)
