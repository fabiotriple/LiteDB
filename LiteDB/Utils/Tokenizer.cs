﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LiteDB
{
    #region TokenType definition

    internal enum TokenType
    {
        /// <summary> { </summary>
        OpenBrace,
        /// <summary> } </summary>
        CloseBrace,
        /// <summary> [ </summary>
        OpenBracket,
        /// <summary> ] </summary>
        CloseBracket,
        /// <summary> ( </summary>
        OpenParenthesis,
        /// <summary> ) </summary>
        CloseParenthesis,
        /// <summary> , </summary>
        Comma,
        /// <summary> : </summary>
        Colon,
        /// <summary> @ </summary>
        At,
        /// <summary> . </summary>
        Period,
        /// <summary> $ </summary>
        Dollar,
        /// <summary> `=` `&gt;` `&lt;` `!=` `&gt;=` `&lt;=` `+` `-` `*` `/` `\` `%` `BETWEEN` </summary>
        Operator,
        String,
        Int,
        Double,
        Word,
        Whitespace,
        EOF,
        Unknown
    }

    #endregion

    #region Token definition

    /// <summary>
    /// Represent a single string token
    /// </summary>
    internal class Token
    {
        public Token(TokenType tokenType, string value, long position)
        {
            this.Position = position;
            this.Value = value;
            this.Type = tokenType;
        }

        public TokenType Type { get; private set; }
        public string Value { get; private set; }
        public long Position { get; private set; }

        public Token Expect(TokenType type)
        {
            if (this.Type != type)
            {
                throw LiteException.UnexpectedToken(this);
            }

            return this;
        }

        /// <summary>
        /// Expect for type1 OR type2
        /// </summary>
        public Token Expect(TokenType type1, TokenType type2)
        {
            if (this.Type != type1 && this.Type != type2)
            {
                throw LiteException.UnexpectedToken(this);
            }

            return this;
        }

        public override string ToString()
        {
            return this.Value + " (" + this.Type + ")";
        }
    }

    #endregion

    /// <summary>
    /// Class to tokenize TextReader input used in JsonRead/BsonExpressions - ASCII char names: https://www.ascii.cl/htmlcodes.htm
    /// This class are not thread safe
    /// </summary>
    internal class Tokenizer
    {
        private TextReader _reader;
        private char _char = '\0';
        private Token _ahead = null;

        public bool EOF { get; private set; }
        public long Position { get; private set; }
        public Token Current { get; private set; }

        /// <summary>
        /// Pre-loaded known keywords
        /// </summary>
        private static HashSet<string> _keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BETWEEN", "LIKE", "AND", "OR" };

        /// <summary>
        /// If EOF throw an invalid token exception (used in while()) otherwise return "false" (not EOF)
        /// </summary>
        public bool CheckEOF()
        {
            if (this.EOF) throw LiteException.UnexpectedToken(this.Current);

            return false;
        }

        public Tokenizer(string source)
            : this(new StringReader(source))
        {
        }

        public Tokenizer(TextReader reader)
        {
            _reader = reader;

            this.Position = 0;
            this.ReadChar();
        }

        /// <summary>
        /// Checks if char is an valid part of a word [a-Z_]+[a-Z0-9_$]*
        /// </summary>
        public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        /// <summary>
        /// Read next char in stream and set in _current
        /// </summary>
        private char ReadChar()
        {
            if (this.EOF) return '\0';

            var c = _reader.Read();

            this.Position++;

            if (c == -1)
            {
                _char = '\0';
                this.EOF = true;
            }
            else
            {
                _char = (char)c;
            }

            return _char;
        }

        /// <summary>
        /// Look for next token but keeps in buffer when run "ReadToken()" again.
        /// </summary>
        public Token LookAhead(bool eatWhitespace = true)
        {
            if (_ahead != null)
            {
                if (eatWhitespace && _ahead.Type == TokenType.Whitespace)
                {
                    _ahead = this.ReadNext(eatWhitespace);
                }

                return _ahead;
            }

            return _ahead = this.ReadNext(eatWhitespace);
        }

        /// <summary>
        /// Read next token (or from ahead buffer).
        /// </summary>
        public Token ReadToken(bool eatWhitespace = true)
        {
            if (_ahead == null)
            {
                return this.Current = this.ReadNext(eatWhitespace);
            }

            if (eatWhitespace && _ahead.Type == TokenType.Whitespace)
            {
                _ahead = this.ReadNext(eatWhitespace);
            }

            this.Current = _ahead;
            _ahead = null;
            return this.Current;
        }

        /// <summary>
        /// Read next token from reader
        /// </summary>
        private Token ReadNext(bool eatWhitespace)
        {
            // remove whitespace before get next token
            if (eatWhitespace) this.EatWhitespace();

            if (this.EOF)
            {
                return new Token(TokenType.EOF, null, this.Position);
            }

            Token token = null;

            switch (_char)
            {
                case '%':
                case '/':
                case '*':
                case '+':
                case '-':
                case '=':
                    token = new Token(TokenType.Operator, _char.ToString(), this.Position);
                    this.ReadChar();
                    break;

                case '!':
                    this.ReadChar();
                    if (_char == '=')
                    {
                        token = new Token(TokenType.Operator, "!=", this.Position);
                        this.ReadChar();
                    }
                    break;

                case '>':
                case '<':
                    var op = _char.ToString();
                    this.ReadChar();
                    if (_char == '=')
                    {
                        op += "=";
                        this.ReadChar();
                    }
                    token = new Token(TokenType.Operator, op, this.Position);
                    break;

                case '[':
                    token = new Token(TokenType.OpenBracket, "[", this.Position);
                    this.ReadChar();
                    break;

                case ']':
                    token = new Token(TokenType.CloseBracket, "]", this.Position);
                    this.ReadChar();
                    break;

                case '{':
                    token = new Token(TokenType.OpenBrace, "{", this.Position);
                    this.ReadChar();
                    break;

                case '}':
                    token = new Token(TokenType.CloseBrace, "}", this.Position);
                    this.ReadChar();
                    break;

                case '(':
                    token = new Token(TokenType.OpenParenthesis, "(", this.Position);
                    this.ReadChar();
                    break;

                case ')':
                    token = new Token(TokenType.CloseParenthesis, ")", this.Position);
                    this.ReadChar();
                    break;

                case ',':
                    token = new Token(TokenType.Comma, ",", this.Position);
                    this.ReadChar();
                    break;

                case ':':
                    token = new Token(TokenType.Colon, ":", this.Position);
                    this.ReadChar();
                    break;

                case '.':
                    token = new Token(TokenType.Period, ".", this.Position);
                    this.ReadChar();
                    break;

                case '@':
                    token = new Token(TokenType.At, "@", this.Position);
                    this.ReadChar();
                    break;

                case '$':
                    this.ReadChar();
                    if (IsWordChar(_char))
                    {
                        token = new Token(TokenType.Word, "$" + this.ReadWord(), this.Position);
                    }
                    else
                    {
                        token = new Token(TokenType.Dollar, "$", this.Position);
                    }
                    break;

                case '\"':
                case '\'':
                    token = new Token(TokenType.String, this.ReadString(_char), this.Position);
                    break;

                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    var dbl = false;
                    var number = this.ReadNumber(ref dbl);
                    token = new Token(dbl ? TokenType.Double : TokenType.Int, number, this.Position);
                    break;

                case ' ':
                case '\n':
                case '\r':
                case '\t':
                    var length = 0;
                    while(char.IsWhiteSpace(_char) && !this.EOF)
                    {
                        this.ReadChar();
                        length++;
                    }
                    token = new Token(TokenType.Whitespace, "".PadLeft(length, ' '), this.Position);
                    break;

                default:
                    // read simple word or an operators (BETWEEN, LIKE, AND, OR...)
                    if (IsWordChar(_char))
                    {
                        var word = this.ReadWord();

                        if (_keywords.Contains(word))
                        {
                            token = new Token(TokenType.Operator, word, this.Position);
                        }
                        else
                        {
                            token = new Token(TokenType.Word, word, this.Position);
                        }
                    }
                    break;
            }

            return token ?? new Token(TokenType.Unknown, _char.ToString(), this.Position);
        }

        /// <summary>
        /// Eat all whitespace - used before a valid token
        /// </summary>
        private void EatWhitespace()
        {
            while (char.IsWhiteSpace(_char) && !this.EOF)
            {
                this.ReadChar();
            }
        }

        /// <summary>
        /// Read a word (word = [\w$]+)
        /// </summary>
        private string ReadWord()
        {
            var sb = new StringBuilder();
            sb.Append(_char);

            this.ReadChar();

            while (!this.EOF && IsWordChar(_char))
            {
                sb.Append(_char);
                this.ReadChar();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Read a number - it's accepts all number char, but not validate. When run Convert, .NET will check if number is correct
        /// </summary>
        private string ReadNumber(ref bool dbl)
        {
            var sb = new StringBuilder();
            sb.Append(_char);

            var canDot = true;
            var canE = true;
            var canSign = false;

            this.ReadChar();

            while (!this.EOF &&
                (char.IsDigit(_char) || _char == '+' || _char == '-' || _char == '.' || _char == 'e' || _char == 'E'))
            {
                if (_char == '.')
                {
                    if (canDot == false) break;
                    dbl = true;
                    canDot = false;
                }
                else if (_char == 'e' || _char == 'E')
                {
                    if (canE == false) break;
                    canE = false;
                    canSign = true;
                }
                else if (_char == '-' || _char == '+')
                {
                    if (canSign == false) break;
                    canSign = false;
                }

                sb.Append(_char);
                this.ReadChar();
            }

            return sb.ToString();
        }
        
        /// <summary>
        /// Read a string removing open and close " or '
        /// </summary>
        private string ReadString(char quote)
        {
            var sb = new StringBuilder();
            this.ReadChar(); // remove first " or '

            while (_char != quote && !this.EOF)
            {
                if (_char == '\\')
                {
                    this.ReadChar();

                    if (_char == quote) sb.Append(quote);

                    switch (_char)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            var codePoint = ParseUnicode(this.ReadChar(), this.ReadChar(), this.ReadChar(), this.ReadChar());
                            sb.Append((char)codePoint);
                            break;
                    }
                }
                else
                {
                    sb.Append(_char);
                }

                this.ReadChar();
            }

            this.ReadChar(); // read last " or '

            return sb.ToString();
        }

        public static uint ParseUnicode(char c1, char c2, char c3, char c4)
        {
            uint p1 = ParseSingleChar(c1, 0x1000);
            uint p2 = ParseSingleChar(c2, 0x100);
            uint p3 = ParseSingleChar(c3, 0x10);
            uint p4 = ParseSingleChar(c4, 1);

            return p1 + p2 + p3 + p4;
        }

        public static uint ParseSingleChar(char c1, uint multiplier)
        {
            uint p1 = 0;
            if (c1 >= '0' && c1 <= '9')
                p1 = (uint)(c1 - '0') * multiplier;
            else if (c1 >= 'A' && c1 <= 'F')
                p1 = (uint)((c1 - 'A') + 10) * multiplier;
            else if (c1 >= 'a' && c1 <= 'f')
                p1 = (uint)((c1 - 'a') + 10) * multiplier;
            return p1;
        }

        public override string ToString()
        {
            return this.Current?.ToString() + " [ahred: " + _ahead?.ToString() + "] - position: " + this.Position;
        }
    }
}