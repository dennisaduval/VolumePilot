from dataclasses import replace
import hashlib
from pathlib import Path
import tempfile
import unittest

from PIL import Image

from pilot_teambuilder.composition import compose
from pilot_teambuilder.model import Rect
from pilot_teambuilder.rendering import TemplateCache, discover_templates, render
from pilot_teambuilder.rows import RowTable
from pilot_teambuilder.synthetic import athletes, write_sources


class RenderingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.bg=self.root/"Team Background_Default.JPG"
        self.fg=self.root/"Team Foreground_default.png"
        with Image.new("RGB",(900,300),"#0B1F3A") as image:
            image.save(self.bg)
        with Image.new("RGBA",(900,300),(255,0,0,0)) as image:
            image.putpixel((0,0),(0,255,0,255))
            image.save(self.fg)

    def test_defaults_case_and_duplicates(self):
        self.assertEqual(discover_templates(self.root),(self.bg,self.fg))
        (self.root/"Team Background_default.jpeg").write_bytes(self.bg.read_bytes())
        with self.assertRaisesRegex(ValueError,"Duplicate"):
            discover_templates(self.root)

    def test_invalid_template_ratio_and_alpha(self):
        cache=TemplateCache()
        self.addCleanup(cache.close)
        with Image.new("RGB",(300,300)) as image:
            image.save(self.bg)
        with self.assertRaisesRegex(ValueError,"3:1"):
            cache.load(self.bg,False)
        with Image.new("RGBA",(900,300),"white") as image:
            image.save(self.fg)
        with self.assertRaisesRegex(ValueError,"transparent"):
            cache.load(self.fg,True)

    def test_cache_reuse_and_bound(self):
        cache=TemplateCache()
        self.addCleanup(cache.close)
        first=cache.load(self.bg,False)
        self.assertIs(first,cache.load(self.bg,False))
        cache.load(self.fg,True)
        alternative=self.root/"alternative.jpg"
        alternative.write_bytes(self.bg.read_bytes())
        cache.load(alternative,False)
        self.assertEqual(len(cache.layers),2)

    def test_full_jpeg_dimensions_dpi_footer_foreground_and_no_overwrite(self):
        people=athletes(3)
        sources=write_sources(self.root/"sources",people)
        hashes={key:hashlib.sha256(path.read_bytes()).hexdigest() for key,path in sources.items()}
        layout=compose(people,RowTable({3:(3,)}))
        output=self.root/"final.jpg"
        result=render(layout,sources,self.bg,self.fg,"Test Team",output)
        self.assertGreater(result["timings"]["total_seconds"],0)
        with Image.open(output) as image:
            self.assertEqual(image.size,(7800,2400))
            self.assertEqual(image.info["dpi"],(300,300))
            self.assertEqual(image.mode,"RGB")
            self.assertGreater(image.getpixel((0,0))[1],100)
            self.assertEqual(image.crop((7200,0,7800,2400)).getextrema()[0],(0,255))
        with self.assertRaises(FileExistsError):
            render(layout,sources,self.bg,self.fg,"Test Team",output)
        self.assertEqual(hashes,{k:hashlib.sha256(p.read_bytes()).hexdigest() for k,p in sources.items()})
        self.assertFalse(list(self.root.glob(".team-*")))

    def test_source_changes_fail_without_publishing(self):
        people=athletes(1)
        sources=write_sources(self.root/"sources",people)
        layout=compose(people,RowTable({1:(1,)}))
        with Image.new("RGBA",(300,300)) as image:
            image.save(next(iter(sources.values())))
        output=self.root/"final.jpg"
        with self.assertRaisesRegex(ValueError,"dimensions changed"):
            render(layout,sources,self.bg,self.fg,"Test",output)
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
