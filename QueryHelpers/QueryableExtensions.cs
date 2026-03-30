using System.Linq.Expressions;
using System.Reflection;

namespace identityServer.QueryHelpers;

public static class QueryableExtensions
{

    public static IQueryable<T> ApplySort<T>(this IQueryable<T> query, string? sort, string? order)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return query;

        var property = typeof(T).GetProperty(sort, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
            return query;

        var param = Expression.Parameter(typeof(T), "x");
        var access = Expression.MakeMemberAccess(param, property);
        var lambda = Expression.Lambda(access, param);

        var methodName = string.Equals(order, "DESC", StringComparison.OrdinalIgnoreCase)
            ? "OrderByDescending"
            : "OrderBy";

        var result = Expression.Call(
            typeof(Queryable), methodName,
            [typeof(T), property.PropertyType],
            query.Expression, Expression.Quote(lambda));

        return query.Provider.CreateQuery<T>(result);
    }


    public static IQueryable<T> ApplySearch<T>(
        this IQueryable<T> query,
        string? search,
        params Expression<Func<T, string?>>[] properties)
    {
        if (string.IsNullOrWhiteSpace(search) || properties.Length == 0)
            return query;

        var param = Expression.Parameter(typeof(T), "x");
        var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
        var containsMethod = typeof(string).GetMethod("Contains", [typeof(string)])!;
        var searchValue = Expression.Constant(search.ToLower());

        Expression? body = null;
        foreach (var prop in properties)
        {
            var member = ReplaceParameter(prop.Body, prop.Parameters[0], param);

            var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
            var toLower = Expression.Call(member, toLowerMethod);
            var contains = Expression.Call(toLower, containsMethod, searchValue);
            var check = Expression.AndAlso(notNull, contains);

            body = body is null ? check : Expression.OrElse(body, check);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(body!, param);
        return query.Where(predicate);
    }


    public static IQueryable<T> ApplyFilters<T>(this IQueryable<T> query, Dictionary<string, string>? filters)
    {
        if (filters is null || filters.Count == 0)
            return query;

        foreach (var (key, value) in filters)
        {
            var property = typeof(T).GetProperty(key, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
                continue;

            object? converted;
            try
            {
                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                converted = Convert.ChangeType(value, targetType);
            }
            catch
            {
                continue;
            }

            var param = Expression.Parameter(typeof(T), "x");
            var member = Expression.MakeMemberAccess(param, property);

            Expression body;
            if (property.PropertyType == typeof(string))
            {
                var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
                var containsMethod = typeof(string).GetMethod("Contains", [typeof(string)])!;
                var searchValue = Expression.Constant(value.ToLower());
                var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
                var toLower = Expression.Call(member, toLowerMethod);
                var contains = Expression.Call(toLower, containsMethod, searchValue);
                body = Expression.AndAlso(notNull, contains);
            }
            else
            {
                var constant = Expression.Constant(converted, property.PropertyType);
                body = Expression.Equal(member, constant);
            }

            var predicate = Expression.Lambda<Func<T, bool>>(body, param);

            query = query.Where(predicate);
        }

        return query;
    }

    public static IQueryable<T> ApplyPagination<T>(this IQueryable<T> query, int page, int pageSize)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 10;

        return query.Skip((page - 1) * pageSize).Take(pageSize);
    }

    private static Expression ReplaceParameter(Expression expr, ParameterExpression oldParam, ParameterExpression newParam)
    {
        return new ParameterReplacer(oldParam, newParam).Visit(expr);
    }

    private sealed class ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == oldParam ? newParam : base.VisitParameter(node);
    }
}
