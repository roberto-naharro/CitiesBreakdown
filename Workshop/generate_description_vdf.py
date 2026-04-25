#!/usr/bin/env python3
"""Generate /tmp/description.vdf for steamcmd — description-only update (no content upload).

Used by .github/workflows/workshop-update-description.yml.
Omits contentfolder and previewfile so only the Workshop page description is updated.
Uses descriptionfile so steamcmd reads actual newlines from the file.
"""
import os

workspace = os.environ['GITHUB_WORKSPACE']
item_id = os.environ.get('WORKSHOP_ITEM_ID', '')

if not item_id:
    raise SystemExit("ERROR: WORKSHOP_ITEM_ID secret is not set. Cannot update description without a Workshop item ID.")

desc_path = os.path.join(workspace, 'Workshop', 'description.txt')

vdf = (
    '"workshopitem"\n'
    '{\n'
    '\t"appid"\t\t\t"255710"\n'
    '\t"publishedfileid"\t"' + item_id + '"\n'
    '\t"descriptionfile"\t"' + desc_path + '"\n'
    '\t"changenote"\t\t"Description update"\n'
    '}\n'
)

with open('/tmp/description.vdf', 'w') as f:
    f.write(vdf)

print("Generated /tmp/description.vdf:")
print(vdf)
