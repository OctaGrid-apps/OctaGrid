using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Application;

internal static class Extensions
{
    public static TEnum ParseOrThrow<TEnum>(this string value) where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, true, out TEnum result))
        {
            return result;
        }
        throw new ValidationException(typeof(TEnum).ToString(), "Unknow value");
    }

    public static UpdateSettersBuilder<TSource> SetPropertyIfNotNull<TSource, TProperty>(
        this UpdateSettersBuilder<TSource> setter,
        Expression<Func<TSource, TProperty>> propertyExpression,
        TProperty valueExpression)
    {
        if (valueExpression is null)
        {
            return setter;
        }
        return setter.SetProperty(propertyExpression, valueExpression);
    }

    public static IQueryable<TSource> WhereIf<TSource>(this IQueryable<TSource> source, bool condition, Expression<Func<TSource, bool>> predicate)
    {
        return condition ? source.Where(predicate) : source;
    }
}
