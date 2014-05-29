KopiLuaInteface
===============

This is a combination of LuaInterface 2.0.3 (Using https://github.com/stevedonovan/MonoLuaInterface) with KopiLua.  
It is largly based on https://github.com/gfoot/kopiluainterface The idea is to provide a pure C# Lua suite for maximum portability in environments like
Unity and XNA.

Note that KopiLua is in a submodule along with a test suite, so after 
cloning KopiLuaInterface you'll need to "git submodule update --init" 
to fill in the KopiLua directory.

What is KopiLua?
----------------

KopiLua is a pure C# Lua implementation - mostly a direct transliteration 
of the standard C implementation.  If you're happy to use the C-style API
and write your own interfacing code on top of it then you can use KopiLua 
on its own, without KopiLuaInterface.

See the documentation in the KopiLua directory for more information.

What is KopiLuaInterface?
-------------------------

KopiLuaInterface is a version of LuaInterface altered to run on top of 
KopiLua.  LuaInterface provides very flexible and user-friendly object
oriented C#/Lua interfacing, making it very easy to provide your Lua 
code with access to C# data and methods, and vice versa.  It is powerful 
and elegant.

