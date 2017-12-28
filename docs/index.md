---
title: Home
layout: default
---

#Introduction
DHGMS Quality Assurance Pack is a starter nuget package to stick in projects that bundles together various Roslyn Analyzers and other tools that can be handy in Quality Assurance of a development project. The projects bundled into this are subjective and may not suit your tastes, but it's a good proof of concept on how you can do something similar for your own team.

#Packages
This is the collection of packages bundled into this pack. This shows how you can get started quickly for each project you create by having a starting package that references all the nuget packages you desire.

{% for nugetpackages in site.data.nugetpackages %}
<h2><a href="{{ nugetpackages.uri }}">{{ nugetpackages.name }}</a></h2>
{% endfor %}
