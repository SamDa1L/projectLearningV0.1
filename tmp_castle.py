# -*- coding: utf-8 -*-
from pathlib import Path
text = Path("Docs/monster-system-0.2-plan.md").read_text(encoding="utf-8")
start = text.index("3. **CastleDB Demo 工程搭建**")
end = text.index("**验收标准**")
print(repr(text[start:end]))
