﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Text;
using Rhetos.Utilities;
using Rhetos.Compiler;
using Rhetos.Dsl;
using Rhetos.Dsl.DefaultConcepts;
using Rhetos.Extensibility;

namespace Rhetos.Dom.DefaultConcepts
{
    [Export(typeof(IConceptCodeGenerator))]
    [ExportMetadata(MefProvider.Implements, typeof(InvalidDataInfo))]
    public class InvalidDataCodeGenerator : IConceptCodeGenerator
    {
        public static readonly CsTag<InvalidDataInfo> UserMessageAppend = "UserMessage";

        public void GenerateCode(IConceptInfo conceptInfo, ICodeBuilder codeBuilder)
        {
            var info = (InvalidDataInfo)conceptInfo;
            codeBuilder.InsertCode(CheckInvalidItemsSnippet(info), WritableOrmDataStructureCodeGenerator.OnSaveTag2, info.Source);
            codeBuilder.AddReferencesFromDependency(typeof(UserException));
        }

        private static string CheckInvalidItemsSnippet(InvalidDataInfo info)
        {
            return string.Format(
@"            if (inserted.Length > 0 || updated.Length > 0)
            {{
                {0}[] changedItems = inserted.Concat(updated).ToArray();
                var invalidItems = _domRepository.{0}.Filter(QueryPersisted(changedItems), new {1}());
                if (invalidItems.Count() > 0)
                    throw new Rhetos.UserException({2}, ""DataStructure:{0},ID:"" + invalidItems.First().ID.ToString(){3});

                // Workaround to restore NH proxies if NHSession.Clear() is called inside filter.
                for (int i=0; i<inserted.Length; i++) inserted[i] = _executionContext.NHibernateSession.Load<{0}>(inserted[i].ID);
                for (int i=0; i<updated.Length; i++) updated[i] = _executionContext.NHibernateSession.Load<{0}>(updated[i].ID);
            }}
",
                info.Source.GetKeyProperties(),
                info.FilterType,
                CsUtility.QuotedString(info.ErrorMessage),
                UserMessageAppend.Evaluate(info));
        }
    }
}
