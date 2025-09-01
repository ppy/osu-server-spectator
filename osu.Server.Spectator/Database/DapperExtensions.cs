// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Data;
using System.Threading;
using Dapper;

namespace osu.Server.Spectator.Database
{
    public static class DapperExtensions
    {
        // see https://stackoverflow.com/questions/12510299/get-datetime-as-utc-with-dapper
        public class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
        {
            public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
            {
                switch (parameter.DbType)
                {
                    case DbType.DateTime:
                    case DbType.DateTime2:
                    case DbType.AnsiString: // Seems to be some MySQL type mapping here
                        parameter.Value = value.UtcDateTime;
                        break;

                    case DbType.DateTimeOffset:
                    case DbType.String: // TODO: i don't know why it's coming in as a string but let's just try and handle it for now?
                        parameter.Value = value;
                        break;

                    default:
                        throw new InvalidOperationException("Must be DateTime or DateTimeOffset object to be mapped.");
                }
            }

            public override DateTimeOffset Parse(object value)
            {
                switch (value)
                {
                    case DateTime time:
                        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc), TimeSpan.Zero);

                    case DateTimeOffset dto:
                        return dto;

                    default:
                        throw new InvalidOperationException("Must be DateTime or DateTimeOffset object to be mapped.");
                }
            }
        }

        private static int dateTimeOffsetMapperInstalled;

        public static void InstallDateTimeOffsetMapper()
        {
            // Assumes SqlMapper.ResetTypeHandlers() is never called.
            if (Interlocked.CompareExchange(ref dateTimeOffsetMapperInstalled, 1, 0) == 0)
            {
                // First remove the default type map between typeof(DateTimeOffset) => DbType.DateTimeOffset (not valid for MySQL)
                SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
                SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));

                // This handles nullable value types automatically e.g. DateTimeOffset?
                SqlMapper.AddTypeHandler(typeof(DateTimeOffset), new DateTimeOffsetTypeHandler());
            }
        }
    }
}
